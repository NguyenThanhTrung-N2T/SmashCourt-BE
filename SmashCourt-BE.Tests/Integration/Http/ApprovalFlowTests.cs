using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-3 Task 17: duyệt, từ chối, thứ tự kiểm, đơn tại quầy, cờ tắt, trả cọc sau duyệt, check-out, hết hạn trả cọc qua HTTP thật
/// (BE-IT-06, 07, 12, 16, 19; spec 9.4). TADSS là WireMock; giá 200.000 đ/giờ, đặt 1 giờ.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed partial class ApprovalFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : ApprovalTestBase(database, host)
{
    [GeneratedRegex(@"/booking/cancel\?token=([A-Za-z0-9_\-%]+)")]
    private static partial Regex CancelLinkPattern();

    // ───────────────────────── Duyệt cọc > 0 (BE-IT-06, 07) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task Approve30Percent_SendsTheOverrideFromJwt_StoresManagerDeposit_WithoutPayment()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();
        var before = DateTime.UtcNow;

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);
        var after = DateTime.UtcNow;

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal("Duyệt đơn thành công, đã thông báo khách thanh toán cọc", call.Envelope.GetProperty("message").GetString());
        Assert.Equal("APPROVED", call.DataString("approvalResult"));
        Assert.Equal("PENDING", call.DataString("status"));
        Assert.Equal(0.3m, call.DataDecimal("depositRate"));
        Assert.Equal(60_000m, call.DataDecimal("depositAmount"));
        Assert.Equal(140_000m, call.DataDecimal("remainingAmount"));
        Assert.Equal("MANAGER", call.DataString("depositSource"));
        Assert.Equal(manager.Id, call.Data.GetProperty("resolvedBy").GetGuid());

        // Request gửi TADSS: người duyệt từ JWT, khóa chống trùng theo quyết định + hành động
        var request = Assert.Single(await tadss.WaitForOverridesAsync(pending.DecisionId, 1)).RequestMessage!;
        Assert.Equal(manager.Id.ToString(), request.Headers!["X-Actor-Id"].Single());
        Assert.Equal($"{pending.DecisionId}:APPROVE", request.Headers["X-Idempotency-Key"].Single());
        var body = JsonNode.Parse(request.Body!)!.AsObject();
        Assert.Equal("APPROVE", body["action"]!.GetValue<string>());
        Assert.Equal(0.3m, body["deposit"]!["rate"]!.GetValue<decimal>());
        Assert.Equal(Reason, body["reason"]!.GetValue<string>());

        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(ApprovalResult.APPROVED, booking.ApprovalResult);
        Assert.Equal(manager.Id, booking.ApprovalResolvedBy);
        Assert.Equal(Reason, booking.ApprovalResolutionNote);
        Assert.Equal(DepositSource.MANAGER, booking.Invoice!.DepositSource);
        Assert.Equal(60_000m, booking.Invoice.DepositAmount);
        // Q2: hạn trả cọc = hạn giữ chỗ mới = lúc duyệt + 15' (giờ chơi còn xa), slot lock cùng mốc
        var expiresAt = booking.Invoice.ExpiresAt!.Value;
        Assert.InRange(expiresAt, before.AddMinutes(15).AddSeconds(-1), after.AddMinutes(15).AddSeconds(1));
        Assert.Equal(expiresAt, booking.Invoice.HoldDeadline);
        Assert.All(booking.SlotLocks, l => Assert.Equal(expiresAt, l.ExpiresAt));
        Assert.Empty(booking.Invoice.Payments); // P6-10: không tạo Payment/URL khi duyệt

        var email = Assert.Single(factory.Emails.To(pending.Customer.Email));
        Assert.Contains("/bookings", email.Body);
        Assert.DoesNotContain("vnpay", email.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task AfterApproval_CustomerPaysTheDepositThroughRetryPayment_AndIpnConfirms()
    {
        // E2E-02 phía BE: chờ duyệt ⇒ duyệt 30 % ⇒ "Thanh toán cọc" ⇒ IPN (Task 15 kéo về 6A-3)
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m));
        var holdDeadline = (await ReadApprovalBookingAsync(pending.BookingId)).Invoice!.HoldDeadline;

        var customer = await CustomerClientAsync(pending.Customer);
        var retry = await SendAsync(customer.PostAsync($"/api/me/bookings/{pending.BookingId}/retry-payment", null));

        await AssertApiStatusAsync(HttpStatusCode.OK, retry);
        Assert.Equal(60_000m, VnPayAmount(retry.DataString("paymentUrl")!));
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(holdDeadline, booking.Invoice!.ExpiresAt); // không kéo dài quá hạn giữ chỗ (Q2)
        var payment = Assert.Single(booking.Invoice.Payments);
        Assert.Equal(60_000m, payment.Amount);

        using var ipn = await (await CreateAnonymousClientAsync())
            .GetAsync($"/api/payments/vnpay/ipn?{SignedIpnQuery(payment.TransactionRef ?? "", 60_000m)}");
        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);

        var paid = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, paid.Status);
        Assert.Equal(InvoicePaymentStatus.PARTIALLY_PAID, paid.Invoice!.PaymentStatus);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task ApprovedThenAbandoned_PaymentExpiryJobCancelsAndFreesTheSlot()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m));
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == pending.BookingId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-60))
                .SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        await RunPaymentExpiryJobAsync();

        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.All(booking.BookingCourts, c => Assert.False(c.IsActive));
        // B1 (6A-5): đã duyệt mà khách chưa bấm thanh toán (chưa có Payment) vẫn là lỗi của khách, không phải SYSTEM_CLEANUP
        Assert.Equal(CancelSourceEnum.SYSTEM, booking.CancelSource);
        Assert.Equal(BookingCancelReason.PAYMENT_EXPIRED, booking.CancelReason);
        Assert.Null(booking.CancelledBy);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, booking.Invoice!.PaymentStatus);
        Assert.Empty(booking.SlotLocks);
    }

    // ───────────────────────── Duyệt 0 % (Q4) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task ApproveZero_ConfirmsPostpaid_CountsThePromotionOnce_AndTheEmailCancelLinkWorks()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 3);
        var pending = await CreatePendingApprovalAsync(tadss, promotionId: promotion.Id);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0m, 0m);
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0m);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal("Duyệt đơn thành công, đơn đã xác nhận, thu tiền tại quầy", call.Envelope.GetProperty("message").GetString());
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal(0m, call.DataDecimal("depositAmount"));
        Assert.Equal(190_000m, call.DataDecimal("remainingAmount"));
        Assert.Equal(JsonValueKind.Null, call.Data.GetProperty("expiresAt").ValueKind);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        Assert.Equal(PaymentTiming.POSTPAID, booking.Invoice!.PaymentTiming);
        Assert.Empty(booking.SlotLocks);
        Assert.Equal(4, await ReadUsedCountAsync(promotion.Id));

        var email = Assert.Single(factory.Emails.To(pending.Customer.Email));
        var token = Uri.UnescapeDataString(CancelLinkPattern().Match(email.Body).Groups[1].Value);
        using var info = await (await CreateAnonymousClientAsync()).GetAsync($"/api/bookings/cancel/{Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task FreeOrderWith100PercentPromotion_ApprovedAt30Percent_TadssReturnsZero_ConfirmsPostpaid()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m);
        var pending = await CreatePendingApprovalAsync(tadss, promotionId: promotion.Id);
        Assert.Equal(0m, (await ReadApprovalBookingAsync(pending.BookingId)).Invoice!.FinalTotal);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 0m);

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task ApproveZero_ThenCheckInAndCheckout_CollectsTheWholeTotalAtTheCounter()
    {
        // Spec 9.4 dòng "cọc 0": thu toàn bộ tại quầy
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0m, 0m);
        var managerClient = await CreateClientForAsync(manager);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(managerClient, pending.BookingId, 0m));
        // Dời giờ chơi về "vừa bắt đầu 5 phút" để check-in được (cửa sổ ±15')
        var vnNow = DateTimeHelper.GetVietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        var start = TimeOnly.FromDateTime(vnNow.AddMinutes(-5));
        start = new TimeOnly(start.Hour, start.Minute);
        if (start > TimeOnly.FromDateTime(vnNow))
            start = TimeOnly.MinValue; // vừa qua nửa đêm
        // Giờ kết thúc phải cùng ngày (chk_booking_courts_valid_time): sau 23:00 cộng một giờ sẽ vòng qua 00:xx
        var end = start < new TimeOnly(23, 0) ? start.AddHours(1) : new TimeOnly(23, 59);
        await WithDbAsync(db => db.Bookings.Where(b => b.Id == pending.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.BookingDate, today)));
        await WithDbAsync(db => db.BookingCourts.Where(c => c.BookingId == pending.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Date, today).SetProperty(c => c.StartTime, start)
                .SetProperty(c => c.EndTime, end)));

        using var checkIn = await managerClient.PostAsync($"/api/bookings/{pending.BookingId}/check-in", null);
        Assert.True(checkIn.IsSuccessStatusCode, await checkIn.Content.ReadAsStringAsync());
        using var checkout = await managerClient.PostAsync($"/api/bookings/{pending.BookingId}/checkout", null);
        Assert.True(checkout.IsSuccessStatusCode, await checkout.Content.ReadAsStringAsync());

        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        var cash = Assert.Single(booking.Invoice.Payments);
        Assert.Equal(PaymentTxMethod.CASH, cash.Method);
        Assert.Equal(200_000m, cash.Amount);
    }

    // ───────────────────────── Từ chối (BE-IT-06) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-06")]
    public async Task Reject_SendsRejectWithoutDeposit_CancelsReleasesNotifiesAndEmails()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss, twoCourts: true);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "REJECT", manager.Id.ToString(), Reason);
        var waiter = $"cho-slot-{Guid.NewGuid():N}@test.local";
        await WithDbAsync(async db =>
        {
            db.SlotInterests.Add(new SlotInterest
            {
                Id = Guid.NewGuid(), CourtId = pending.Court.CourtId, Date = FutureDate(), StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(11, 0), Email = waiter, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            return await db.SaveChangesAsync();
        });
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();

        var call = await RejectAsync(await CreateClientForAsync(manager), pending.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal("Đã từ chối đơn và giải phóng sân", call.Envelope.GetProperty("message").GetString());
        Assert.Equal("REJECTED", call.DataString("approvalResult"));
        Assert.Equal("CANCELLED", call.DataString("status"));
        Assert.Equal(JsonValueKind.Null, call.Data.GetProperty("depositSource").ValueKind);
        var request = Assert.Single(await tadss.WaitForOverridesAsync(pending.DecisionId, 1)).RequestMessage!;
        Assert.Equal($"{pending.DecisionId}:REJECT", request.Headers!["X-Idempotency-Key"].Single());
        Assert.False(JsonNode.Parse(request.Body!)!.AsObject().ContainsKey("deposit")); // bỏ hẳn khóa deposit

        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(BookingCancelReason.APPROVAL_REJECTED, booking.CancelReason);
        Assert.Equal(CancelSourceEnum.STAFF, booking.CancelSource);
        Assert.Equal(manager.Id, booking.CancelledBy);
        Assert.Equal(ApprovalResult.REJECTED, booking.ApprovalResult);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, booking.Invoice!.PaymentStatus);
        Assert.Equal(2, booking.BookingCourts.Count);
        Assert.All(booking.BookingCourts, c => Assert.False(c.IsActive)); // đơn nhiều sân: mọi sân nhả
        Assert.Empty(booking.SlotLocks);

        var rejection = Assert.Single(factory.Emails.To(pending.Customer.Email));
        Assert.DoesNotContain(Reason, rejection.Body);
        Assert.Single(factory.Emails.To(waiter)); // người chờ slot được báo
    }

    [Fact]
    [Trait("Catalog", "BE-IT-06")]
    public async Task AfterReject_AnotherCustomerCanBookTheSameSlot()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "REJECT", manager.Id.ToString(), Reason);
        await AssertApiStatusAsync(HttpStatusCode.OK, await RejectAsync(await CreateClientForAsync(manager), pending.BookingId));

        var (call, _) = await BookAsync(pending.Court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
    }

    // ───────────────────────── Thứ tự kiểm (BE-IT-12) ─────────────────────────

    [Theory]
    [InlineData(1.5, Reason, "DepositRate")]
    [InlineData(0.12345, Reason, "DepositRate")]
    [InlineData(0.3, "abc", "Reason")]
    [InlineData(0.3, "     ", "Reason")]
    [Trait("Catalog", "BE-IT-12")]
    public async Task InvalidBody_Is400OnTheField_WithoutTadss(double rate, string reason, string field)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, (decimal)rate, reason);

        await AssertApiStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("VALIDATION_ERROR", call.Code);
        Assert.True(call.Envelope.GetProperty("errors").TryGetProperty(field, out var _), call.Raw);
        Assert.Empty(await tadss.WaitForOverridesAsync(pending.DecisionId, 0));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-12")]
    public async Task DepositAboveZeroButBelowMinimum_Is400OnDepositRate_WithoutTadss()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.01m); // 2.000 đ

        await AssertApiStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %",
            call.Envelope.GetProperty("errors").GetProperty("DepositRate")[0].GetString());
        Assert.Empty(await tadss.WaitForOverridesAsync(pending.DecisionId, 0));
        Assert.Equal(BookingStatus.PENDING_APPROVAL, (await ReadApprovalBookingAsync(pending.BookingId)).Status);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-12")]
    public async Task UnknownBooking_Is404()
    {
        var owner = await SeedUserAsync(UserRole.OWNER);

        var call = await ApproveAsync(await CreateClientForAsync(owner), Guid.NewGuid(), 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.NotFound, call);
        Assert.Equal("NOT_FOUND", call.Code);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-12")]
    public async Task ExpiredApproval_Is409Expired_WithoutTadss_AndBookingUnchanged()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        await MoveApprovalExpiryAsync(pending.BookingId, DateTime.UtcNow.AddSeconds(-1));
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = (await ReadApprovalBookingAsync(pending.BookingId)).SlotLocks.Select(l => l.ExpiresAt).ToList();

        var call = await RejectAsync(await CreateClientForAsync(manager), pending.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.Conflict, call);
        Assert.Equal("APPROVAL_EXPIRED", call.Code);
        Assert.Empty(await tadss.WaitForOverridesAsync(pending.DecisionId, 0));
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING_APPROVAL, booking.Status);
        Assert.Equal(locksBefore, booking.SlotLocks.Select(l => l.ExpiresAt).ToList());
    }

    // ───────────────────────── Đơn tại quầy (BE-IT-16), cờ tắt (BE-IT-19) ─────────────────────────

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [Trait("Catalog", "BE-IT-16")]
    public async Task WalkInBooking_Is409NotPendingApproval_WithoutTadss(string endpoint)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        var owner = await SeedUserAsync(UserRole.OWNER);
        var client = await CustomerClientAsync(owner);
        using var created = await client.PostAsJsonAsync("/api/bookings/walk-in", new
        {
            bookingDate = FutureDate().ToString("yyyy-MM-dd"),
            courts = new[] { new { courtId = court.CourtId, startTime = "10:00:00", endTime = "11:00:00" } },
            guestName = "Khách tại quầy",
            guestPhone = "0900000000"
        });
        var walkInId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetGuid();

        var call = endpoint == "approve" ? await ApproveAsync(client, walkInId, 0.3m) : await RejectAsync(client, walkInId);

        await AssertApiStatusAsync(HttpStatusCode.Conflict, call);
        Assert.Equal("BOOKING_NOT_PENDING_APPROVAL", call.Code);
        await Task.Delay(300);
        Assert.Empty(tadss.AllOverrides);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-19")]
    public async Task FlagTurnedOffWhileWaiting_ApprovalStillGoesThroughTadss()
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
            pending = await CreatePendingApprovalAsync(tadss);
        // Cờ đã tắt (Q7): cờ chỉ chi phối việc đánh giá lúc đặt sân
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Single(await tadss.WaitForOverridesAsync(pending.DecisionId, 1));
    }
}
