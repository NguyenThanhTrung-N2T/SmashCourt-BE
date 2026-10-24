using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-2c Task 4: lượt dùng khuyến mãi theo từng nhánh (D3, P-10…P-22) và thứ tự chọn nhánh theo ngưỡng tiền (D5, Z-10…Z-12, Z-18)
/// qua HTTP. <c>UsedCount</c> chỉ tăng khi đơn chắc chắn: a′ ngay lúc đặt, a/d ở IPN, b khi được duyệt (6A-3), c không bao giờ.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Promotion")]
public sealed class OnlineBookingPromotionTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : OnlineBookingTestBase(database, host)
{
    // ───────────────────────── Lượt dùng theo nhánh (D3) ─────────────────────────

    [Fact]
    [Trait("Catalog", "P-10")]
    public async Task FlagOff_FreeOrder_IsConfirmedAndCountsTheUse()
    {
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(0m, call.DataDecimal("finalTotal"));
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal("FULL_PAYMENT_FALLBACK", call.DataString("depositSource"));
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(PaymentTiming.POSTPAID, booking.Invoice?.PaymentTiming);
        Assert.Empty(booking.Invoice?.Payments ?? []);
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));
    }

    [Theory]
    [Trait("Catalog", "P-11")]
    [Trait("Catalog", "P-12")]
    [InlineData(3_000)]
    [InlineData(0)]
    public async Task ApprovedBelowTheMinimum_WithPromotion_CountsTheUse(int deposit)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0m, deposit);
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(190_000m, call.DataDecimal("finalTotal"));
        var logs = (await GetFactoryAsync()).Logs.Entries.Where(e => e.Category.Contains("Tadss") || e.Category.Contains("Deposit")).TakeLast(4);
        Assert.True(call.DataString("status") == "CONFIRMED", $"{call.Envelope}{Environment.NewLine}{string.Join(Environment.NewLine, logs)}");
        Assert.Equal("TADSS", call.DataString("depositSource"));
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));
    }

    public static TheoryData<string> BranchesThatDoNotCountAtBookingTime => ["a", "b", "c", "d"];

    [Theory]
    [Trait("Catalog", "P-13")]
    [Trait("Catalog", "P-15")]
    [Trait("Catalog", "P-16")]
    [MemberData(nameof(BranchesThatDoNotCountAtBookingTime))]
    public async Task OtherBranches_DoNotTouchTheUsageAtBookingTime(string branch)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        switch (branch)
        {
            case "a": tadss.ApproveAnyBooking(0.5m, 95_000m); break;
            case "b": tadss.RequireApprovalForAnyBooking(DateTimeOffset.UtcNow.AddMinutes(20)); break;
            case "c": tadss.RejectAnyBooking(); break;
            default: tadss.Reply(503, null); break;
        }
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 3);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(branch == "c" ? HttpStatusCode.BadRequest : HttpStatusCode.Created, call);
        Assert.Equal(3, await ReadUsedCountAsync(promotion.Id));
        if (branch is "a" or "b")
        {
            var booking = await RequireBookingAsync(call.BookingId);
            Assert.Equal(PaymentTiming.PREPAID, booking.Invoice?.PaymentTiming);
            Assert.NotNull(booking.BookingPromotion);
        }
    }

    [Fact]
    [Trait("Catalog", "P-14")]
    public async Task BranchD_CountsTheUseWhenTheIpnArrives()
    {
        // Hồi quy đường IPN: đơn d có khuyến mãi tăng lượt khi tiền về, không phải lúc đặt
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m);
        var (call, _) = await BookAsync(court, promotion.Id);
        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(0, await ReadUsedCountAsync(promotion.Id));
        var payment = Assert.Single((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []);
        Assert.Equal(190_000m, payment.Amount);
        Assert.Equal(190_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));

        using var ipn = await (await CreateAnonymousClientAsync())
            .GetAsync($"/api/payments/vnpay/ipn?{SignedIpnQuery(payment.TransactionRef ?? "", payment.Amount)}");

        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, booking.Status);
        Assert.Equal(PaymentTxStatus.SUCCESS, Assert.Single(booking.Invoice?.Payments ?? []).Status);
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));
    }

    // ───────────────────────── Giới hạn lượt (hồi quy) ─────────────────────────

    [Fact]
    [Trait("Catalog", "P-17")]
    public async Task UsageLimitOne_SecondCustomerIsBlockedAfterTheFirstConfirmedUse()
    {
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m, usageLimit: 1);

        var (first, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id);
        await AssertStatusAsync(HttpStatusCode.Created, first);
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));

        var secondCourt = await SeedCourtAsync(200_000m);
        var (second, _) = await BookAsync(secondCourt, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.BadRequest, second);
        Assert.Equal("Mã khuyến mãi đã hết lượt sử dụng", second.Message);
        Assert.Null(await FindBookingOnCourtAsync(secondCourt.CourtId));
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "P-18")]
    public async Task UsageLimitTwo_WithOneUsed_AllowsExactlyOneMore()
    {
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m, usageLimit: 2, usedCount: 1);

        var (first, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id);
        await AssertStatusAsync(HttpStatusCode.Created, first);
        Assert.Equal(2, await ReadUsedCountAsync(promotion.Id));

        var (next, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id);
        await AssertStatusAsync(HttpStatusCode.BadRequest, next);
        Assert.Equal("Mã khuyến mãi đã hết lượt sử dụng", next.Message);
    }

    [Fact]
    [Trait("Catalog", "P-19")]
    public async Task UsagePerUserLimit_BlocksTheSameCustomerTheSecondTime()
    {
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m, usagePerUserLimit: 1);
        var customer = await SeedUserAsync();

        var (first, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id, customer);
        await AssertStatusAsync(HttpStatusCode.Created, first);

        var (second, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id, customer);
        await AssertStatusAsync(HttpStatusCode.BadRequest, second);
        Assert.Equal("Bạn đã sử dụng hết lượt áp dụng mã khuyến mãi này", second.Message);

        // Khách khác vẫn dùng được
        var (other, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id);
        await AssertStatusAsync(HttpStatusCode.Created, other);
    }

    // ───────────────────────── Hủy và job ─────────────────────────

    [Fact]
    [Trait("Catalog", "P-20")]
    public async Task BranchAPrime_CancelledByStaff_GivesTheUseBack_WithoutRefund()
    {
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m, usedCount: 4);
        var (call, _) = await BookAsync(court, promotion.Id);
        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(5, await ReadUsedCountAsync(promotion.Id));
        var staffClient = await CustomerClientAsync(await SeedStaffAsync(court.BranchId));

        using var cancel = await staffClient.PostAsync($"/api/bookings/{call.BookingId}/cancel", null);

        Assert.True(cancel.StatusCode == HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        Assert.Equal(4, await ReadUsedCountAsync(promotion.Id));
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.UNPAID, booking.Invoice?.PaymentStatus);
        Assert.False(await WithDbAsync(db => db.Refunds.AnyAsync(r => r.Payment.Invoice.BookingId == call.BookingId)));
    }

    [Fact]
    [Trait("Catalog", "P-21")]
    public async Task AbandonedPaymentOrder_CancelledByJob01_DoesNotTouchTheUsage()
    {
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 2);
        var (call, _) = await BookAsync(court, promotion.Id);
        await AssertStatusAsync(HttpStatusCode.Created, call);
        // Khách bỏ ngang: hạn thanh toán đã qua (lùi cả created_at vì chk_invoices_expires_at đòi expires_at > created_at)
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == call.BookingId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-20))
                .SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        await WithDbAsync(async db =>
        {
            var factory = await GetFactoryAsync();
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBookingJob>().CancelExpiredPendingBookingsAsync();
            return 0;
        });

        Assert.Equal(BookingStatus.CANCELLED, (await RequireBookingAsync(call.BookingId)).Status);
        Assert.Equal(2, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "P-22")]
    public async Task LimitReachedBetweenTheTwoTransactions_KnownLimitation_ConfirmsAnyway()
    {
        // Hạn chế có sẵn, ghi nhận: giới hạn kiểm lúc tạo (transaction 1), lượt tăng ở transaction 2; đơn khác dùng lượt cuối trong lúc
        // chờ TADSS thì đơn a′ vẫn CONFIRMED và UsedCount vượt giới hạn 1. Không đổi hành vi ở 6A-2.
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 3_000);
        tadss.ApproveAnyBooking(0m, 0m, delay: TimeSpan.FromMilliseconds(800));
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usageLimit: 1);

        var booking = BookAsync(court, promotion.Id);
        await WaitUntilAsync(() => FindBookingOnCourtAsync(court.CourtId), b => b != null, TimeSpan.FromSeconds(5));
        await WithDbAsync(db => db.Promotions.Where(p => p.Id == promotion.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, 1)));
        var (call, _) = await booking;

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal(2, await ReadUsedCountAsync(promotion.Id));
    }

    // ───────────────────────── Ngưỡng tiền và thứ tự nhánh (D5) ─────────────────────────

    [Fact]
    [Trait("Catalog", "Z-10")]
    public async Task FlagOff_OrderOf3000_IsConfirmedPayAtCounter()
    {
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 197_000m);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(3_000m, call.DataDecimal("finalTotal"));
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal("FULL_PAYMENT_FALLBACK", call.DataString("depositSource"));
        Assert.Equal((0m, 0m, 3_000m), (call.DataDecimal("depositRate"), call.DataDecimal("depositAmount"), call.DataDecimal("remainingAmount")));
        Assert.Equal(JsonValueKind.Null, call.Data.GetProperty("paymentUrl").ValueKind);
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(PaymentTiming.POSTPAID, booking.Invoice?.PaymentTiming);
        Assert.Empty(booking.Invoice?.Payments ?? []);
        Assert.Equal(1, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "Z-11")]
    public async Task FreeOrder_NeedApproval_StillWaitsForTheManager()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.RequireApprovalForAnyBooking(DateTimeOffset.UtcNow.AddMinutes(20));
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(0m, call.DataDecimal("finalTotal"));
        Assert.Equal("PENDING_APPROVAL", call.DataString("status"));
        Assert.Empty((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []);
        Assert.Equal(0, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "Z-12")]
    public async Task FreeOrder_Rejected_IsStillRejected()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.RejectAnyBooking();
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("BOOKING_REJECTED", call.Code);
        var booking = await FindBookingOnCourtAsync(court.CourtId) ?? throw new InvalidOperationException("Không thấy booking");
        Assert.Equal(BookingCancelReason.POLICY_REJECTED, booking.CancelReason);
        Assert.NotNull(booking.TadssDecisionId);
        Assert.Empty(await ReadLocksAsync(booking.Id));
        Assert.Equal(0, await ReadUsedCountAsync(promotion.Id));
    }

    [Fact]
    [Trait("Catalog", "Z-18")]
    public async Task ApprovedDepositExactlyAtTheMinimum_IsChargedOnline()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.025m, 5_000m);

        var (call, _) = await BookAsync(await SeedCourtAsync(200_000m));

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("PENDING", call.DataString("status"));
        Assert.Equal(5_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        Assert.Equal(5_000m, Assert.Single((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []).Amount);
    }

    // ───────────────────────── N-28: khuyến mãi số tiền cố định ─────────────────────────

    [Fact]
    [Trait("Catalog", "N-28")]
    public async Task FixedAmountPromotion_AboveOneHundred_WorksOnlineAndAtTheCounter()
    {
        // Trước bản sửa schema (phase6_fix_promotion_snapshot.sql) cột snapshot là numeric(5,2) kèm CHECK ≤ 100 ⇒ cả hai luồng lỗi 500
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 20_000m);

        var (online, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id);

        await AssertStatusAsync(HttpStatusCode.Created, online);
        Assert.Equal(180_000m, online.DataDecimal("finalTotal"));
        var snapshot = (await RequireBookingAsync(online.BookingId)).BookingPromotion;
        Assert.Equal((DiscountTypeEnum.FIXED, 20_000m, 20_000m),
            (snapshot?.DiscountTypeSnapshot, snapshot?.DiscountValueSnapshot, snapshot?.DiscountAmount));

        var counterCourt = await SeedCourtAsync(200_000m);
        var owner = await CustomerClientAsync(await SeedUserAsync(UserRole.OWNER));
        var customer = await SeedUserAsync();
        using var walkIn = await owner.PostAsync("/api/bookings/walk-in", JsonContent.Create(new
        {
            bookingDate = FutureDate().ToString("yyyy-MM-dd"),
            courts = new[] { new { courtId = counterCourt.CourtId, startTime = "10:00:00", endTime = "11:00:00" } },
            customerId = customer.Id,
            promotionId = promotion.Id
        }));

        var text = await walkIn.Content.ReadAsStringAsync();
        Assert.True(walkIn.StatusCode == HttpStatusCode.Created, text);
        Assert.Equal(180_000m, JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("finalTotal").GetDecimal());
    }
}
