using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Helpers;
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
