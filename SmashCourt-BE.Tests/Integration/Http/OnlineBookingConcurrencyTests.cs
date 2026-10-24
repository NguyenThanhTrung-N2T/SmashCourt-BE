using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-2c Task 4: chuyện xảy ra trong lúc chờ TADSS.
/// <list type="bullet">
/// <item>K-02…K-05 (D6): khách ngắt kết nối sau ~200 ms, BE vẫn chạy tới cùng nên đơn không bị bỏ lửng PENDING thiếu Payment/cọc.</item>
/// <item>G-01…G-04 (D7): nhân viên hủy đơn trong lúc chờ ⇒ transaction 2 thua câu cổng ⇒ khách nhận 409, không ghi đè việc hủy.</item>
/// </list>
/// TADSS giả trả chậm ~1 s nên TimeoutMs của host được nâng tạm lên 3 s; mọi chờ đều theo điều kiện (đọc lại DB), không sleep cố định.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class OnlineBookingConcurrencyTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : OnlineBookingTestBase(database, host)
{
    private static readonly TimeSpan SlowTadss = TimeSpan.FromMilliseconds(1_000);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static void ReplyByBranch(TadssStub tadss, string branch, TimeSpan delay)
    {
        switch (branch)
        {
            case "a": tadss.ApproveAnyBooking(0.5m, 100_000m, delay); break;
            case "a'": tadss.ApproveAnyBooking(0.01m, 2_000m, delay); break;
            case "b": tadss.RequireApprovalForAnyBooking(DateTimeOffset.UtcNow.AddMinutes(20), delay); break;
            case "c": tadss.RejectAnyBooking(delay); break;
            default: throw new ArgumentOutOfRangeException(nameof(branch));
        }
    }

    /// <summary>Gửi yêu cầu đặt sân rồi hủy phía client sau <paramref name="cancelAfter"/>; trả client của khách để gọi tiếp.</summary>
    private async Task<HttpClient> BookAndDisconnectAsync(BookableCourt court, TimeSpan cancelAfter)
    {
        var client = await CustomerClientAsync(await SeedUserAsync());
        using var cancellation = new CancellationTokenSource(cancelAfter);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.PostAsync(OnlinePath, JsonContent(OnlineRequest([court.CourtId], FutureDate())), cancellation.Token));
        return client;
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private Task<Booking?> WaitForBookingAsync(Guid courtId, Func<Booking, bool> done) =>
        WaitUntilAsync(() => FindBookingOnCourtAsync(courtId), b => b != null && done(b), WaitLimit);

    // ───────────────────────── K: khách ngắt kết nối (D6) ─────────────────────────

    [Fact]
    [Trait("Catalog", "K-02")]
    public async Task Disconnect_Approved_BookingStillGetsItsDepositAndPayment()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 3_000);
        ReplyByBranch(tadss, "a", SlowTadss);
        var court = await SeedCourtAsync(200_000m);

        var client = await BookAndDisconnectAsync(court, TimeSpan.FromMilliseconds(200));

        var booking = await WaitForBookingAsync(court.CourtId, b => b.Invoice?.Payments.Count == 1);
        Assert.NotNull(booking);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal((0.5m, 100_000m, DepositSource.TADSS), (booking.Invoice?.DepositRate, booking.Invoice?.DepositAmount, booking.Invoice?.DepositSource));
        Assert.Equal(100_000m, Assert.Single(booking.Invoice?.Payments ?? []).Amount);
        // Khách quay lại lấy link thanh toán được
        using var retry = await client.PostAsync($"/api/me/bookings/{booking.Id}/retry-payment", null);
        Assert.True(retry.StatusCode == HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Catalog", "K-03")]
    public async Task Disconnect_Rejected_BookingIsStillRejectedAndTheSlotFreed()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 3_000);
        ReplyByBranch(tadss, "c", SlowTadss);
        var court = await SeedCourtAsync(200_000m);

        var client = await BookAndDisconnectAsync(court, TimeSpan.FromMilliseconds(200));

        var booking = await WaitForBookingAsync(court.CourtId, b => b.Status == BookingStatus.CANCELLED);
        Assert.NotNull(booking);
        Assert.Equal(BookingCancelReason.POLICY_REJECTED, booking.CancelReason);
        Assert.NotNull(booking.TadssDecisionId);
        Assert.Empty(await ReadLocksAsync(booking.Id));
        // Không lách REJECTED bằng thanh toán lại
        using var retry = await client.PostAsync($"/api/me/bookings/{booking.Id}/retry-payment", null);
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        Assert.Empty((await RequireBookingAsync(booking.Id)).Invoice?.Payments ?? []);
    }

    [Fact]
    [Trait("Catalog", "K-04")]
    public async Task Disconnect_NeedApproval_BookingStillWaitsForTheManager()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 3_000);
        ReplyByBranch(tadss, "b", SlowTadss);
        var court = await SeedCourtAsync(200_000m);

        var client = await BookAndDisconnectAsync(court, TimeSpan.FromMilliseconds(200));

        var booking = await WaitForBookingAsync(court.CourtId, b => b.Status == BookingStatus.PENDING_APPROVAL);
        Assert.NotNull(booking);
        Assert.NotNull(booking.TadssDecisionId);
        // Không lách duyệt bằng thanh toán lại
        using var retry = await client.PostAsync($"/api/me/bookings/{booking.Id}/retry-payment", null);
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        Assert.Empty((await RequireBookingAsync(booking.Id)).Invoice?.Payments ?? []);
    }

    [Theory]
    [Trait("Catalog", "K-05")]
    [InlineData(200_000, true)]  // d
    [InlineData(3_000, false)]   // a′ (đơn dưới mức tối thiểu)
    public async Task Disconnect_TadssTimesOutTwice_BookingStillEndsInAFinalBranch(int price, bool fullPayment)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync();
        tadss.ReplyTooSlowly();
        var court = await SeedCourtAsync(price);

        await BookAndDisconnectAsync(court, TimeSpan.FromMilliseconds(200));

        var booking = await WaitForBookingAsync(court.CourtId, b => b.Invoice?.DepositSource != null);
        Assert.NotNull(booking);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, booking.Invoice?.DepositSource);
        if (fullPayment)
        {
            Assert.Equal(BookingStatus.PENDING, booking.Status);
            Assert.Equal(price, Assert.Single(booking.Invoice?.Payments ?? []).Amount);
        }
        else
        {
            Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
            Assert.Empty(booking.Invoice?.Payments ?? []);
        }
    }

    // ───────────────────────── G: thua câu cổng (D7) ─────────────────────────

    /// <summary>
    /// Khách đặt (TADSS trả chậm); khi booking PENDING xuất hiện trong DB, nhân viên chi nhánh hủy qua API; trả response của khách và
    /// booking sau cùng.
    /// </summary>
    private async Task<(BookingCall Call, Booking Booking)> StaffCancelsWhileWaitingAsync(BookableCourt court, Guid? promotionId = null)
    {
        var staffClient = await CustomerClientAsync(await SeedStaffAsync(court.BranchId));
        var booking = BookAsync(court, promotionId);

        var pending = await WaitForBookingAsync(court.CourtId, b => b.Status == BookingStatus.PENDING);
        Assert.NotNull(pending);
        using var cancel = await staffClient.PostAsync($"/api/bookings/{pending.Id}/cancel", null);
        Assert.True(cancel.StatusCode == HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        var (call, _) = await booking;
        return (call, await RequireBookingAsync(pending.Id));
    }

    private async Task AssertLostGateAsync(BookingCall call, Booking booking)
    {
        await AssertStatusAsync(HttpStatusCode.Conflict, call);
        Assert.Equal("CONFLICT", call.Code);
        Assert.Equal("Đơn đặt sân vừa được nhân viên xử lý, vui lòng xem lịch sử đặt sân", call.Message);
        Assert.Equal(JsonValueKind.Null, call.Data.ValueKind);
        // Việc hủy của nhân viên giữ nguyên, transaction 2 không ghi gì
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(CancelSourceEnum.STAFF, booking.CancelSource);
        // Transaction 2 không ghi đè lý do của nhân viên thành lý do của nhánh (POLICY_REJECTED)
        Assert.Equal(BookingCancelReason.STAFF_CANCELLED, booking.CancelReason);
        Assert.Null(booking.TadssDecisionId);
        Assert.Null(booking.CancelTokenHash);
        Assert.Null(booking.Invoice?.DepositSource);
        Assert.Empty(booking.Invoice?.Payments ?? []);
        var factory = await GetFactoryAsync();
        Assert.Contains(factory.Logs.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains("thua câu cập nhật có điều kiện") && e.Message.Contains(booking.Id.ToString()));
    }

    [Theory]
    [Trait("Catalog", "G-01")]
    [Trait("Catalog", "G-03")]
    [Trait("Catalog", "G-04")]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    public async Task StaffCancelsDuringTheDecision_CustomerGets409_AndTheCancellationStands(string branch)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 3_000);
        ReplyByBranch(tadss, branch, SlowTadss);
        var court = await SeedCourtAsync(200_000m);

        var (call, booking) = await StaffCancelsWhileWaitingAsync(court);

        // c cũng là 409, không phải BOOKING_REJECTED; b không thành PENDING_APPROVAL
        await AssertLostGateAsync(call, booking);
        Assert.Null(booking.ApprovalExpiresAt);
    }

    [Fact]
    [Trait("Catalog", "G-02")]
    public async Task StaffCancelsASmallDepositOrder_NoUseIsCounted()
    {
        // Nhánh a′ thua câu cập nhật có điều kiện nên không tăng lượt; nhân viên hủy đơn online chưa có tiền về cũng không trả lượt
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 3_000);
        ReplyByBranch(tadss, "a'", SlowTadss);
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usedCount: 3);

        var (call, booking) = await StaffCancelsWhileWaitingAsync(court, promotion.Id);

        await AssertLostGateAsync(call, booking);
        Assert.Equal(3, await ReadUsedCountAsync(promotion.Id));
    }
}
