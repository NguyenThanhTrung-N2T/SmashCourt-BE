using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Hủy, hoàn tiền và check-out trên host thật: khách và nhân viên hủy được đơn chưa trả / chờ duyệt, tiền hoàn tính trên số đã thu
/// (tiền mặt tại chi nhánh), khách không tự hủy sau giờ bắt đầu, check-out thu đúng phần còn lại.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class CancellationFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    // ───────────────────────── BE-IT-23: hủy đơn chưa thanh toán ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task CustomerCancelsAnUnpaidOrder_FreesTheSlotAndKeepsThePromotionUse()
    {
        // Khuyến mãi giới hạn 1 lượt/khách: đơn đã hủy không được giữ lượt của khách
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 3, usagePerUserLimit: 1);
        var order = await BookPendingAsync(promotionId: promotion.Id);
        var before = DateTime.UtcNow;

        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));

        var after = DateTime.UtcNow;
        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(CancelSourceEnum.CUSTOMER, booking.CancelSource);
        Assert.Equal(BookingCancelReason.CUSTOMER_CANCELLED, booking.CancelReason);
        Assert.InRange(booking.CancelledAt!.Value, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.Equal(PaymentTxStatus.FAILED, Assert.Single(booking.Invoice!.Payments).Status);
        Assert.Empty(booking.SlotLocks);
        Assert.All(booking.BookingCourts, bc => Assert.False(bc.IsActive));
        Assert.Empty(await ReadRefundsAsync(order.BookingId));
        Assert.Equal(3, await ReadUsedCountAsync(promotion.Id));

        // Khách đặt lại được với cùng khuyến mãi, cùng khung giờ vừa nhả
        var again = await PostOnlineAsync(await CustomerClientAsync(order.Customer),
            OnlineRequest([order.Court.CourtId], FutureDate(), promotionId: promotion.Id));
        await AssertStatusAsync(HttpStatusCode.Created, again);
    }

    [Theory]
    [Trait("Catalog", "BE-IT-23")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingApprovalOrder_CustomerOrStaffCancel_LeavesTheQueue(bool byCustomer)
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
            pending = await CreatePendingApprovalAsync(tadss);
        var staff = await SeedStaffAsync(pending.Court.BranchId);

        var call = byCustomer
            ? await CustomerCancelAsync(pending.Customer, pending.BookingId)
            : await StaffCancelAsync(staff, pending.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(byCustomer ? BookingCancelReason.CUSTOMER_CANCELLED : BookingCancelReason.STAFF_CANCELLED, booking.CancelReason);
        Assert.Empty(booking.SlotLocks);
        var queue = await QueueAsync(await CreateClientForAsync(await SeedManagerAsync(pending.Court.BranchId)),
            $"?branchId={pending.Court.BranchId}");
        Assert.DoesNotContain(pending.BookingId.ToString(), queue.Raw);
    }

    // ───────────────────────── P6-31: ai hủy — nguồn hủy cho mọi người, tên nhân viên chỉ cho nhân viên ─────────────────────────

    /// <summary>Ba cách nhân viên xem một đơn (chi tiết theo id, chi tiết đầy đủ, dòng trong danh sách) — đều phải nạp người hủy.</summary>
    private async Task<List<JsonElement>> StaffViewsAsync(HttpClient staffClient, Guid bookingId)
    {
        var byId = await SendAsync(staffClient.GetAsync($"/api/bookings/{bookingId}"));
        await AssertApiStatusAsync(HttpStatusCode.OK, byId);
        var details = await SendAsync(staffClient.GetAsync($"/api/bookings/details/{bookingId}"));
        await AssertApiStatusAsync(HttpStatusCode.OK, details);
        var list = await SendAsync(staffClient.GetAsync($"/api/bookings?bookingCode={byId.DataString("bookingCode")}&pageSize=50"));
        await AssertApiStatusAsync(HttpStatusCode.OK, list);
        var row = list.Data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == bookingId);
        return [byId.Data, details.Data, row];
    }

    /// <summary>Hai cách khách xem đơn của mình: chi tiết và lịch sử.</summary>
    private async Task<List<JsonElement>> CustomerViewsAsync(User customer, Guid bookingId)
    {
        var client = await CustomerClientAsync(customer);
        var detail = await SendAsync(client.GetAsync($"/api/me/bookings/{bookingId}"));
        await AssertApiStatusAsync(HttpStatusCode.OK, detail);
        var history = await SendAsync(client.GetAsync("/api/me/bookings?pageSize=50"));
        await AssertApiStatusAsync(HttpStatusCode.OK, history);
        var row = history.Data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == bookingId);
        return [detail.Data, row];
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task StaffCancel_StaffSeeWhoCancelled_CustomerSeesOnlyThatTheBranchCancelled()
    {
        var order = await BookPendingAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));

        // Người xem là nhân viên khác cùng chi nhánh: tên phải là của người đã hủy, không phải người đang xem
        var viewer = await SeedStaffAsync(order.Court.BranchId);
        await WithDbAsync(db => db.Users.Where(u => u.Id == viewer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FullName, "Người Xem")));
        foreach (var view in await StaffViewsAsync(await CreateClientForAsync(viewer), order.BookingId))
        {
            Assert.Equal("STAFF", view.GetProperty("cancelSource").GetString());
            Assert.Equal(staff.FullName, view.GetProperty("cancelledByName").GetString());
            Assert.Equal("STAFF_CANCELLED", view.GetProperty("cancelReason").GetString());
        }

        foreach (var view in await CustomerViewsAsync(order.Customer, order.BookingId))
        {
            Assert.Equal("STAFF", view.GetProperty("cancelSource").GetString());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("cancelledByName").ValueKind);
        }
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task ManagerRejects_StaffEndpointsShowTheManagerName_CustomerDoesNot()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        await WithDbAsync(db => db.Users.Where(u => u.Id == manager.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FullName, "Trần Thị Quản Lý")));
        tadss.ReplyOverride(pending.DecisionId, "REJECT", manager.Id.ToString(), Reason);
        var managerClient = await CreateClientForAsync(manager);

        await AssertApiStatusAsync(HttpStatusCode.OK, await RejectAsync(managerClient, pending.BookingId));

        foreach (var view in await StaffViewsAsync(managerClient, pending.BookingId))
        {
            Assert.Equal("STAFF", view.GetProperty("cancelSource").GetString());
            Assert.Equal("Trần Thị Quản Lý", view.GetProperty("cancelledByName").GetString());
            Assert.Equal("APPROVAL_REJECTED", view.GetProperty("cancelReason").GetString());
        }
        foreach (var view in await CustomerViewsAsync(pending.Customer, pending.BookingId))
            Assert.Equal(JsonValueKind.Null, view.GetProperty("cancelledByName").ValueKind);
    }

    [Theory]
    [Trait("Catalog", "BE-IT-23")]
    [InlineData(false, "CUSTOMER")]
    [InlineData(true, "LINK")]
    public async Task CustomerOrLinkCancel_ShowsTheSourceAndNoStaffName(bool byLink, string expectedSource)
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookDepositPaidAsync();
        var call = byLink
            ? await LinkCancelAsync(await GiveCancelLinkAsync(order.BookingId))
            : await CustomerCancelAsync(order.Customer, order.BookingId);
        await AssertApiStatusAsync(HttpStatusCode.OK, call);

        var staff = await SeedStaffAsync(order.Court.BranchId);
        foreach (var view in (await StaffViewsAsync(await CreateClientForAsync(staff), order.BookingId))
                 .Concat(await CustomerViewsAsync(order.Customer, order.BookingId)))
        {
            Assert.Equal(expectedSource, view.GetProperty("cancelSource").GetString());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("cancelledByName").ValueKind);
            Assert.Equal("CUSTOMER_CANCELLED", view.GetProperty("cancelReason").GetString());
        }
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task ActiveBooking_HasNullCancelSourceAndCanceller()
    {
        var order = await BookPendingAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);

        foreach (var view in (await StaffViewsAsync(await CreateClientForAsync(staff), order.BookingId))
                 .Concat(await CustomerViewsAsync(order.Customer, order.BookingId)))
        {
            Assert.Equal(JsonValueKind.Null, view.GetProperty("cancelSource").ValueKind);
            Assert.Equal(JsonValueKind.Null, view.GetProperty("cancelledByName").ValueKind);
        }
    }

    // ───────────────────────── BE-IT-10: hoàn tiền theo chính sách ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task StaffCancelsADepositOrder_RefundsOnTheDepositNotTheTotal()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookDepositPaidAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        Assert.Equal(BookingCancelReason.STAFF_CANCELLED, booking.CancelReason);
        var refund = Assert.Single(await ReadRefundsAsync(order.BookingId));
        Assert.Equal(60_000m, refund.Amount); // mã cũ hoàn 200.000 đ
        Assert.Equal(100m, refund.RefundPercent);

        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));
        Assert.Equal(60_000m, (await RequireBookingAsync(order.BookingId)).Invoice!.Payments.Single(p => p.Status == PaymentTxStatus.SUCCESS).RefundedAmount);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task LinkPreviewAndCancel_ShowAndRefundTheSameAmount()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookDepositPaidAsync();
        var raw = await GiveCancelLinkAsync(order.BookingId);

        var preview = await LinkPreviewAsync(raw);
        await AssertApiStatusAsync(HttpStatusCode.OK, preview);
        Assert.Equal(60_000m, preview.DataDecimal("refundAmount"));

        await AssertApiStatusAsync(HttpStatusCode.OK, await LinkCancelAsync(raw));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(CancelSourceEnum.LINK, booking.CancelSource);
        Assert.Equal(BookingCancelReason.CUSTOMER_CANCELLED, booking.CancelReason);
        Assert.Equal(60_000m, Assert.Single(await ReadRefundsAsync(order.BookingId)).Amount);
    }

    [Fact]
    public async Task LinkOfAnUncancellableOrder_FailsAndStaysUsable()
    {
        // Mã cũ đánh dấu link "đã dùng" trước khi kiểm, nên link chết dù đơn không bị hủy
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        var raw = await GiveCancelLinkAsync(order.BookingId);

        var call = await LinkCancelAsync(raw);

        Assert.Equal(HttpStatusCode.BadRequest, call.Status);
        Assert.Null((await RequireBookingAsync(order.BookingId)).CancelTokenUsedAt);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task CustomerAfterTheStartTime_CannotCancelButStaffCan()
    {
        var order = await BookPaidAsync();
        await MovePlayToJustStartedAsync(order.BookingId);

        var customer = await CustomerCancelAsync(order.Customer, order.BookingId);

        Assert.Equal(HttpStatusCode.BadRequest, customer.Status);
        Assert.Equal(ErrorCodes.BookingAlreadyStarted, customer.Code);
        Assert.Equal(BookingStatus.PAID_ONLINE, (await RequireBookingAsync(order.BookingId)).Status);

        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));
    }

    // ───────────────────────── BE-IT-10: check-out ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task CheckoutOfADepositOrder_CollectsTheRestOfTheCourtAndTheServices()
    {
        var order = await BookDepositPaidAsync();
        await StartPlayingAsync(order.BookingId);
        await AddServiceFeeAsync(order.BookingId, 20_000m);
        var staff = await SeedStaffAsync(order.Court.BranchId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(staff, order.BookingId));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        var cash = Assert.Single(booking.Invoice.Payments, p => p.Method == PaymentTxMethod.CASH);
        Assert.Equal(160_000m, cash.Amount); // 140.000 đ tiền sân còn lại + 20.000 đ dịch vụ
        Assert.Equal(220_000m, booking.Invoice.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task CheckoutOfAFullyPaidOrder_CollectsOnlyTheServices()
    {
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        await AddServiceFeeAsync(order.BookingId, 20_000m);
        var staff = await SeedStaffAsync(order.Court.BranchId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(staff, order.BookingId));

        var cash = Assert.Single((await RequireBookingAsync(order.BookingId)).Invoice!.Payments, p => p.Method == PaymentTxMethod.CASH);
        Assert.Equal(20_000m, cash.Amount);
    }

    [Theory]
    [Trait("Catalog", "BE-IT-10")]
    [InlineData(30, 100)] // mốc 24 giờ
    [InlineData(20, 50)]  // mốc 12 giờ; mã cũ lệch 7 giờ (giờ VN trừ giờ UTC) nên tính thành 27 giờ ⇒ 100 %
    [InlineData(5, 20)]   // mốc 2 giờ
    public async Task RefundPercent_FollowsTheHoursLeftInVietnamTime(int hoursAhead, int expectedPercent)
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPaidAsync();
        await MovePlayToHoursAheadAsync(order.BookingId, hoursAhead);

        await AssertApiStatusAsync(HttpStatusCode.OK,
            await StaffCancelAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));

        var refund = Assert.Single(await ReadRefundsAsync(order.BookingId));
        Assert.Equal(expectedPercent, refund.RefundPercent);
        Assert.Equal(200_000m * expectedPercent / 100, refund.Amount);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task DepositPaidTwice_ThenCancelled_RefundsEachPaymentAndConfirmsTheTotal()
    {
        // Một Refund cho mỗi Payment (C2): gộp 120.000 đ vào một Payment 60.000 đ sẽ vi phạm chk_payments_refunded_range
        await EnsureCancelPoliciesAsync();
        var pending = await ApproveDepositAsync();
        await PayBothLinksAsync(pending.Customer, pending.BookingId);
        var staff = await SeedStaffAsync(pending.Court.BranchId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, pending.BookingId));

        var refunds = await ReadRefundsAsync(pending.BookingId);
        Assert.Equal(2, refunds.Count);
        Assert.Equal(2, refunds.Select(r => r.PaymentId).Distinct().Count());
        Assert.All(refunds, r => Assert.Equal(60_000m, r.Amount));
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, (await RequireBookingAsync(pending.BookingId)).Status);
        var factory = await GetFactoryAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, pending.BookingId));

        var booking = await RequireBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, booking.Status);
        Assert.All(booking.Invoice!.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS),
            p => Assert.Equal(p.Amount, p.RefundedAmount));
        Assert.All(await ReadRefundsAsync(pending.BookingId), r => Assert.Equal(RefundStatus.COMPLETED, r.Status));
        var email = Assert.Single(factory.Emails.To(pending.Customer.Email), e => e.Subject.Contains("Xác nhận hoàn tiền"));
        Assert.Contains(120_000m.ToString("N0") + " VNĐ", email.Body);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-22")]
    public async Task MoneyArrivesAfterTheRefundWasConfirmed_AddsANewRefundWithoutTouchingTheOldOne()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPendingAsync();
        var oldLink = order.TransactionRef;
        var newLink = await StartPaymentAsync(order.Customer, order.BookingId);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(newLink.TransactionRef!, newLink.Amount));
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));
        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));
        var firstRefund = Assert.Single(await ReadRefundsAsync(order.BookingId));

        // Link cũ (đã FAILED khi bấm thanh toán lại) vẫn trả được: tiền về sau khi đơn đã hoàn xong
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(oldLink, order.Amount));

        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, (await RequireBookingAsync(order.BookingId)).Status);
        var refunds = await ReadRefundsAsync(order.BookingId);
        Assert.Equal(2, refunds.Count);
        var kept = Assert.Single(refunds, r => r.Id == firstRefund.Id);
        Assert.Equal(RefundStatus.COMPLETED, kept.Status);
        Assert.Equal(firstRefund.ProcessedAt, kept.ProcessedAt);
        var late = Assert.Single(refunds, r => r.Id != firstRefund.Id);
        Assert.Equal((RefundStatus.PENDING, 200_000m, 100m), (late.Status, late.Amount, late.RefundPercent));

        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, booking.Status);
        Assert.Equal(400_000m, booking.Invoice!.Payments.Sum(p => p.RefundedAmount));
        Assert.All(booking.Invoice.Payments, p => Assert.Equal(p.Amount, p.RefundedAmount));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task ConfirmRefundTwice_SecondIsRejectedAndRefundsNothingMore()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPaidAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));
        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));

        var again = await ConfirmRefundAsync(staff, order.BookingId);

        Assert.Equal(HttpStatusCode.BadRequest, again.Status);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, booking.Status);
        Assert.Equal(200_000m, Assert.Single(booking.Invoice!.Payments).RefundedAmount);
        Assert.Single(await ReadRefundsAsync(order.BookingId));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task RetryPaymentAfterTheCustomerCancelled_IsRejectedWithoutANewLink()
    {
        var order = await BookPendingAsync();
        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));

        var retry = await RetryAsync(order.Customer, order.BookingId);

        Assert.Equal(HttpStatusCode.BadRequest, retry.Status);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(PaymentTxStatus.FAILED, Assert.Single(booking.Invoice!.Payments).Status);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task PaidOrderWithAOncePerCustomerPromotion_AfterCancelTheCustomerCanUseItAgain()
    {
        // P6-18 với đơn đã trả: trạng thái sau hủy là CANCELLED_PENDING_REFUND (ca chưa trả ở trên chỉ phủ CANCELLED)
        await EnsureCancelPoliciesAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usagePerUserLimit: 1);
        var order = await BookPaidAsync(promotionId: promotion.Id);
        var client = await CustomerClientAsync(order.Customer);

        var whilePaid = await PostOnlineAsync(client, OnlineRequest([order.Court.CourtId], FutureDate(), hour: 14, promotionId: promotion.Id));
        await AssertStatusAsync(HttpStatusCode.BadRequest, whilePaid);

        await AssertApiStatusAsync(HttpStatusCode.OK,
            await StaffCancelAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, (await RequireBookingAsync(order.BookingId)).Status);

        var afterCancel = await PostOnlineAsync(client, OnlineRequest([order.Court.CourtId], FutureDate(), hour: 14, promotionId: promotion.Id));
        await AssertStatusAsync(HttpStatusCode.Created, afterCancel);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task LinkAfterTheStartTime_BookingAlreadyStarted_AndTheLinkIsNotUsedUp()
    {
        var order = await BookPaidAsync();
        await MovePlayToJustStartedAsync(order.BookingId);
        var raw = await GiveCancelLinkAsync(order.BookingId); // hạn link 1 giờ nữa: chỉ còn chốt "đã tới giờ bắt đầu"

        var call = await LinkCancelAsync(raw);

        Assert.Equal(HttpStatusCode.BadRequest, call.Status);
        Assert.Equal(ErrorCodes.BookingAlreadyStarted, call.Code);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, booking.Status);
        Assert.Null(booking.CancelTokenUsedAt);
        Assert.Empty(await ReadRefundsAsync(order.BookingId));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-25")]
    public async Task PaymentCancelAndRefundTimes_AreUtcInstantsTakenDuringTheRequest()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPendingAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);

        var beforeIpn = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        var afterIpn = DateTime.UtcNow;
        var beforeCancel = DateTime.UtcNow;
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));
        var afterCancel = DateTime.UtcNow;
        var beforeConfirm = DateTime.UtcNow;
        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));
        var afterConfirm = DateTime.UtcNow;

        var booking = await RequireBookingAsync(order.BookingId);
        AssertUtcWithin(Assert.Single(booking.Invoice!.Payments).PaidAt, beforeIpn, afterIpn);
        AssertUtcWithin(booking.CancelledAt, beforeCancel, afterCancel);
        var refund = Assert.Single(await ReadRefundsAsync(order.BookingId));
        AssertUtcWithin(refund.CreatedAt, beforeCancel, afterCancel);
        AssertUtcWithin(refund.ProcessedAt, beforeConfirm, afterConfirm);
    }

    private static void AssertUtcWithin(DateTime? value, DateTime before, DateTime after)
    {
        Assert.NotNull(value);
        Assert.Equal(DateTimeKind.Utc, value.Value.Kind);
        Assert.InRange(value.Value, before, after);
    }

    // ───────────────────────── Trả lượt khuyến mãi khi hủy (7.9 bước 5) ─────────────────────────

    public enum CancelPath { Link, Customer, Staff }

    [Theory]
    [Trait("Catalog", "BE-IT-23")]
    [InlineData(CancelPath.Link)]
    [InlineData(CancelPath.Customer)]
    [InlineData(CancelPath.Staff)]
    public async Task PaidOrderWithPromotion_EveryCancelPath_ReturnsThePromotionUse(CancelPath path)
    {
        // Lỗi 25/10: hủy qua link nạp đơn không kèm BookingPromotion nên lượt đã tính ở IPN không được trả
        await EnsureCancelPoliciesAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 3);
        var order = await BookPaidAsync(promotionId: promotion.Id);
        Assert.Equal(4, await ReadUsedCountAsync(promotion.Id));

        var call = path switch
        {
            CancelPath.Link => await LinkCancelAsync(await GiveCancelLinkAsync(order.BookingId)),
            CancelPath.Customer => await CustomerCancelAsync(order.Customer, order.BookingId),
            _ => await StaffCancelAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId)
        };

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, (await RequireBookingAsync(order.BookingId)).Status);
        Assert.Equal(3, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task ApprovedZeroOrderWithPromotion_LinkCancel_ReturnsThePromotionUse()
    {
        // Đơn thu tại quầy (POSTPAID) đã tính lượt lúc duyệt: hủy qua link email phải trả lại
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 3);
        var pending = await CreatePendingApprovalAsync(tadss, promotionId: promotion.Id);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0m, 0m);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0m));
        Assert.Equal(4, await ReadUsedCountAsync(promotion.Id));

        await AssertApiStatusAsync(HttpStatusCode.OK, await LinkCancelAsync(await GiveCancelLinkAsync(pending.BookingId)));

        Assert.Equal(BookingStatus.CANCELLED, (await RequireBookingAsync(pending.BookingId)).Status);
        Assert.Equal(3, await ReadUsedCountAsync(promotion.Id));
    }

    // ───────────────────────── Nội dung email hủy ─────────────────────────

    private const string NothingPaidText = "Đơn chưa được thanh toán nên không phát sinh khoản hoàn tiền nào.";
    private const string TooLateText = "Đơn này không được hoàn tiền do hủy quá gần giờ chơi.";

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task CancelEmailOfAnUnpaidOrder_SaysNothingWasPaid_NotTooLate()
    {
        // Mã cũ ghi "hủy quá gần giờ chơi" cho mọi đơn hoàn 0 đ, kể cả đơn chưa trả đồng nào hủy trước nhiều ngày
        var order = await BookPendingAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));

        var email = await CancelEmailToAsync(order.Customer.Email);
        Assert.Contains(NothingPaidText, email.Body);
        Assert.DoesNotContain(TooLateText, email.Body);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task CancelEmailOfAPaidOrderAfterTheStart_SaysNoRefundBecauseTooLate()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPaidAsync();
        await MovePlayToJustStartedAsync(order.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK,
            await StaffCancelAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));

        Assert.Equal(BookingStatus.CANCELLED, (await RequireBookingAsync(order.BookingId)).Status);
        var email = await CancelEmailToAsync(order.Customer.Email);
        Assert.Contains(TooLateText, email.Body);
        Assert.DoesNotContain(NothingPaidText, email.Body);
    }

    private async Task<RecordingEmailService.SentEmail> CancelEmailToAsync(string address) =>
        Assert.Single((await GetFactoryAsync()).Emails.To(address), e => e.Subject.Contains("Xác nhận hủy đặt sân"));

    // ───────────────────────── BE-IT-25 (phần 6A-4) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-25")]
    public async Task WalkInForAGuestWithEmail_StoresAUtcCancelLinkAndSendsTheEmail()
    {
        await EnsureTimeSlotsAsync();
        var court = await SeedCourtAsync(200_000m);
        var staff = await SeedStaffAsync(court.BranchId);
        var date = DateTimeHelper.GetTodayInVietnam().AddDays(1);
        var email = $"walkin-{Guid.NewGuid():N}@example.com";
        var before = DateTime.UtcNow;

        using var response = await (await CreateClientForAsync(staff)).PostAsJsonAsync("/api/bookings/walk-in", new
        {
            bookingDate = date.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss"),
            courts = new[] { new { courtId = court.CourtId, startTime = "08:00:00", endTime = "09:00:00" } },
            guestName = "Khách tại quầy",
            guestPhone = "0900000000",
            guestEmail = email
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = DateTime.UtcNow;
        var booking = await FindBookingOnCourtAsync(court.CourtId) ?? throw new InvalidOperationException("Không thấy đơn tại quầy");
        var startUtc = DateTimeHelper.ToUtcFromVietnam(date, new TimeOnly(8, 0));
        // Hạn link = min(giờ bắt đầu, lúc tạo + 24 giờ), lưu theo UTC
        var expires = booking.CancelTokenExpiresAt!.Value;
        Assert.Equal(DateTimeKind.Utc, expires.Kind);
        Assert.InRange(expires, Min(startUtc, before.AddHours(24)).AddSeconds(-1), Min(startUtc, after.AddHours(24)).AddSeconds(1));
        Assert.NotEmpty((await GetFactoryAsync()).Emails.To(email));
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
