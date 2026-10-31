using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-2c, rà soát 23/10: các tình huống thực tế ngoài danh mục của plan.
/// <list type="bullet">
/// <item>Hai khách (hoặc một khách bấm hai lần) đặt cùng sân, cùng giờ, cùng lúc.</item>
/// <item>Thanh toán tiền cọc qua IPN (nhánh a), kể cả IPN bị sửa số tiền.</item>
/// <item>Đặt trúng slot đang được đơn khác giữ.</item>
/// <item>Đơn nhiều sân trùng một phần, trùng một phần thời gian, sát giờ.</item>
/// <item>Tranh lượt khuyến mãi cuối cùng (mở rộng P-22).</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class OnlineBookingRealWorldTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : OnlineBookingTestBase(database, host)
{
    private static readonly string[] SlotTakenCodes = ["SLOT_UNAVAILABLE", "SLOT_UNAVAILABLE_NOTIFY_REGISTERED"];

    private Task<List<SmashCourt_BE.Models.Entities.Booking>> BookingsOnCourtAsync(Guid courtId) =>
        WithDbAsync(db => db.Bookings.AsNoTracking()
            .Include(b => b.Invoice).ThenInclude(i => i!.Payments)
            .Where(b => b.BookingCourts.Any(bc => bc.CourtId == courtId))
            .ToListAsync());

    private static object Slot(Guid courtId, string start, string end) => new { courtId, startTime = start, endTime = end };

    // ───────────────────────── Đặt trùng cùng lúc ─────────────────────────

    [Theory]
    [InlineData(false)] // hai khách khác nhau
    [InlineData(true)]  // một khách bấm hai lần
    public async Task TwoRequestsForTheSameSlotAtOnce_ExactlyOneWins_AndTadssIsAskedOnlyForTheWinner(bool sameCustomer)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m, delay: TimeSpan.FromMilliseconds(200));
        var court = await SeedCourtAsync(200_000m);
        var first = await SeedUserAsync();
        var second = sameCustomer ? first : await SeedUserAsync();
        var request = OnlineRequest([court.CourtId], FutureDate());

        var calls = await Task.WhenAll(
            PostOnlineAsync(await CustomerClientAsync(first), request),
            PostOnlineAsync(await CustomerClientAsync(second), request));

        var winners = calls.Where(c => c.Status == HttpStatusCode.Created).ToList();
        var losers = calls.Where(c => c.Status != HttpStatusCode.Created).ToList();
        Assert.Single(winners);
        var loser = Assert.Single(losers);
        await AssertStatusAsync(HttpStatusCode.BadRequest, loser);
        Assert.Contains(loser.Code, SlotTakenCodes);

        // Không có đơn mồ côi: đúng một đơn còn hiệu lực, có cọc và Payment; đơn thua (nếu kịp tạo ở transaction 1) đã bị rollback
        var bookings = await BookingsOnCourtAsync(court.CourtId);
        var booking = Assert.Single(bookings);
        Assert.Equal(winners[0].BookingId, booking.Id);
        Assert.Equal(DepositSource.TADSS, booking.Invoice?.DepositSource);
        Assert.Single(booking.Invoice?.Payments ?? []);
        Assert.Single(await ReadLocksAsync(booking.Id));
        Assert.Single(await tadss.WaitForEvaluationsAsync(booking.Id, 1));
        await Task.Delay(300);
        Assert.Single(tadss.EvaluationsMentioning(court.CourtId));
    }

    [Fact]
    [Trait("Catalog", "N-29")]
    public async Task ManyRoundsOfThreeSimultaneousRequests_NeverFailWith500()
    {
        // N-29: trước khi sửa, giao dịch Serializable (40001) và deadlock ở exclusion constraint (40P01) làm người thua nhận 500.
        // Nhiều vòng để chắc nhánh tranh chấp ở transaction 1 thật sự được chạy tới.
        for (var round = 0; round < 8; round++)
        {
            var court = await SeedCourtAsync(200_000m);
            var request = OnlineRequest([court.CourtId], FutureDate());
            var clients = new List<HttpClient>();
            for (var i = 0; i < 3; i++)
                clients.Add(await CustomerClientAsync(await SeedUserAsync()));

            var calls = await Task.WhenAll(clients.Select(c => PostOnlineAsync(c, request)));

            foreach (var unexpected in calls.Where(c => c.Status is not (HttpStatusCode.Created or HttpStatusCode.BadRequest)))
                await AssertStatusAsync(HttpStatusCode.BadRequest, unexpected);
            Assert.Single(calls, c => c.Status == HttpStatusCode.Created);
            Assert.All(calls.Where(c => c.Status == HttpStatusCode.BadRequest), c => Assert.Contains(c.Code, SlotTakenCodes));
            Assert.Single(await BookingsOnCourtAsync(court.CourtId));
        }
    }

    // ───────────────────────── Thanh toán cọc qua IPN (nhánh a) ─────────────────────────

    [Fact]
    public async Task BranchA_DepositPaidThroughIpn_BookingIsPaidOnlineWithTheRestDueAtTheCounter()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        var factory = await GetFactoryAsync();
        var (call, customer) = await BookAsync(await SeedCourtAsync(200_000m));
        await AssertStatusAsync(HttpStatusCode.Created, call);
        var payment = Assert.Single((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []);
        Assert.Empty(factory.Emails.To(customer.Email));

        using var ipn = await (await CreateAnonymousClientAsync())
            .GetAsync($"/api/payments/vnpay/ipn?{SignedIpnQuery(payment.TransactionRef ?? "", 100_000m)}");

        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PARTIALLY_PAID, booking.Invoice?.PaymentStatus);
        Assert.Equal(PaymentTxStatus.SUCCESS, Assert.Single(booking.Invoice?.Payments ?? []).Status);
        Assert.Empty(await ReadLocksAsync(call.BookingId));
        // Email xác nhận kèm link hủy gửi lúc tiền về (E-03), không phải lúc đặt
        Assert.Single(factory.Emails.To(customer.Email));
        Assert.NotNull(booking.CancelTokenHash);

        // Khách thấy phần còn lại phải trả tại quầy
        var detail = await (await CustomerClientAsync(customer)).GetFromJsonAsync<JsonElement>($"/api/me/bookings/{call.BookingId}");
        var data = detail.GetProperty("data");
        Assert.Equal("PAID_ONLINE", data.GetProperty("status").GetString());
        Assert.Equal(100_000m, data.GetProperty("depositAmount").GetDecimal());
        Assert.Equal(100_000m, data.GetProperty("remainingAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("approvalReasonCode").ValueKind);
    }

    [Fact]
    public async Task BranchA_IpnWithATamperedAmount_IsIgnored()
    {
        // VNPay báo số tiền khác Payment (VD sửa URL để trả 100 % thay vì cọc, hoặc trả thiếu): không xác nhận đơn
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        var (call, _) = await BookAsync(await SeedCourtAsync(200_000m));
        var payment = Assert.Single((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []);

        foreach (var amount in new[] { 200_000m, 50_000m })
        {
            using var ipn = await (await CreateAnonymousClientAsync())
                .GetAsync($"/api/payments/vnpay/ipn?{SignedIpnQuery(payment.TransactionRef ?? "", amount)}");
            Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        }

        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(PaymentTxStatus.PENDING, Assert.Single(booking.Invoice?.Payments ?? []).Status);
        Assert.Equal(InvoicePaymentStatus.UNPAID, booking.Invoice?.PaymentStatus);
    }

    // ───────────────────────── Khách tự hủy đơn a′ bằng link trong email ─────────────────────────

    [Fact]
    public async Task BranchAPrime_CustomerCancelsThroughTheEmailLink_SlotIsFreedWithoutRefund()
    {
        // Đơn a′ có ngay cả khi cờ tắt (đơn < 5.000 đ): link hủy trong email phải dùng được đầu cuối. Lượt khuyến mãi khi hủy thuộc 7.9 bước 5 (6A-4).
        var court = await SeedCourtAsync(3_000m);
        var date = FutureDate();
        var factory = await GetFactoryAsync();
        var (call, customer) = await BookAsync(court, date: date);
        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("CONFIRMED", call.DataString("status"));
        var email = Assert.Single(factory.Emails.To(customer.Email));
        var token = System.Text.RegularExpressions.Regex.Match(email.Body, @"/booking/cancel\?token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        var anonymous = await CreateAnonymousClientAsync();

        using var cancel = await anonymous.PostAsync($"/api/bookings/cancel/{token}", null);

        Assert.True(cancel.StatusCode == HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.All(booking.BookingCourts, bc => Assert.False(bc.IsActive));
        Assert.False(await WithDbAsync(db => db.Refunds.AnyAsync(r => r.Payment.Invoice.BookingId == call.BookingId)));
        // Bấm link lần nữa: idempotent theo thiết kế có sẵn (trả thành công, không đổi gì); slot đặt lại được
        using var again = await anonymous.PostAsync($"/api/bookings/cancel/{token}", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(BookingStatus.CANCELLED, (await RequireBookingAsync(call.BookingId)).Status);
        var (rebook, _) = await BookAsync(court, date: date);
        await AssertStatusAsync(HttpStatusCode.Created, rebook);
    }

    // ───────────────────────── Đặt trúng slot đang được giữ ─────────────────────────

    [Theory]
    [InlineData("b")] // đơn chờ duyệt giữ slot tới hạn duyệt
    [InlineData("a")] // đơn chờ thanh toán cọc giữ slot tới hạn thanh toán
    public async Task SlotHeldByAnotherOrder_SecondCustomerIsTurnedAway_AndGetsNotifiedLater(string heldBy)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        if (heldBy == "b")
            tadss.RequireApprovalForAnyBooking(DateTimeOffset.UtcNow.AddMinutes(20));
        else
            tadss.ApproveAnyBooking(0.5m, 100_000m);
        var court = await SeedCourtAsync(200_000m);
        var (held, _) = await BookAsync(court);
        await AssertStatusAsync(HttpStatusCode.Created, held);

        var latecomer = await SeedUserAsync();
        var (second, _) = await BookAsync(court, customer: latecomer);

        await AssertStatusAsync(HttpStatusCode.BadRequest, second);
        Assert.Equal("SLOT_UNAVAILABLE_NOTIFY_REGISTERED", second.Code);
        // Đăng ký báo slot trống bằng email tài khoản (P6-01), không có booking thứ hai, TADSS chỉ được hỏi cho đơn đầu
        Assert.True(await WithDbAsync(db => db.SlotInterests.AnyAsync(i =>
            i.CourtId == court.CourtId && i.Email == latecomer.Email.ToLower() && i.CustomerId == latecomer.Id)));
        Assert.Single(await BookingsOnCourtAsync(court.CourtId));
        await Task.Delay(300);
        Assert.Single(tadss.EvaluationsMentioning(court.CourtId));
    }

    // ───────────────────────── Đơn nhiều sân và trùng giờ ─────────────────────────

    [Fact]
    public async Task MultiCourtOrder_WhenOneCourtIsTaken_NothingIsCreatedForTheOtherCourt()
    {
        var court = await SeedCourtAsync(200_000m);
        var second = await SeedSecondCourtAsync(court);
        var date = FutureDate();
        var otherClient = await CustomerClientAsync(await SeedUserAsync());
        await AssertStatusAsync(HttpStatusCode.Created, await PostOnlineAsync(otherClient, OnlineRequest([second], date)));

        var client = await CustomerClientAsync(await SeedUserAsync());
        var call = await PostOnlineAsync(client, OnlineRequest([court.CourtId, second], date));

        await AssertStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Contains(call.Code, SlotTakenCodes);
        Assert.Empty(await BookingsOnCourtAsync(court.CourtId));
        Assert.False(await WithDbAsync(db => db.SlotLocks.AnyAsync(l => l.CourtId == court.CourtId)));
        Assert.Single(await BookingsOnCourtAsync(second));
    }

    public static TheoryData<string, string, bool> TimeOverlaps => new()
    {
        // khung mới so với đơn có sẵn 10:00–11:00: bắt đầu, kết thúc, bị chặn
        { "10:30:00", "11:30:00", true },  // chồng nửa sau
        { "09:30:00", "10:30:00", true },  // chồng nửa trước
        { "10:15:00", "10:45:00", true },  // nằm trong
        { "09:00:00", "12:00:00", true },  // bao trùm
        { "11:00:00", "12:00:00", false }, // sát sau: không chồng
        { "09:00:00", "10:00:00", false }, // sát trước: không chồng
    };

    [Theory]
    [MemberData(nameof(TimeOverlaps))]
    public async Task OverlappingTimesOnTheSameCourt_AreRejected_AdjacentOnesAreNot(string start, string end, bool blocked)
    {
        var court = await SeedCourtAsync(200_000m);
        var date = FutureDate();
        var (existing, _) = await BookAsync(court, date: date, hour: 10);
        await AssertStatusAsync(HttpStatusCode.Created, existing);
        var client = await CustomerClientAsync(await SeedUserAsync());

        var call = await PostOnlineAsync(client, new { bookingDate = date.ToString("yyyy-MM-dd"), courts = new[] { Slot(court.CourtId, start, end) } });

        if (blocked)
        {
            await AssertStatusAsync(HttpStatusCode.BadRequest, call);
            Assert.Contains(call.Code, SlotTakenCodes);
            Assert.Single(await BookingsOnCourtAsync(court.CourtId));
        }
        else
        {
            await AssertStatusAsync(HttpStatusCode.Created, call);
            Assert.Equal(2, (await BookingsOnCourtAsync(court.CourtId)).Count);
        }
    }

    // ───────────────────────── Tranh lượt khuyến mãi cuối (mở rộng P-22) ─────────────────────────

    [Fact]
    [Trait("Catalog", "P-22")]
    public async Task TwoCustomersRaceForTheLastPromotionUse_NoUseIsLost_AndTheLimitHoldsAfterwards()
    {
        // Giới hạn kiểm ở transaction 1, lượt tăng ở transaction 2 (hạn chế đã ghi nhận ở P-22): khi tranh nhau, cả hai có thể được
        // nhận nên UsedCount có thể vượt giới hạn 1. Điều phải giữ: không mất lượt (UsedCount = số đơn đã tính lượt) và sau đó mã bị chặn.
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m, usageLimit: 1);
        var courts = new[] { await SeedCourtAsync(200_000m), await SeedCourtAsync(200_000m) };

        var calls = await Task.WhenAll(courts.Select(async court =>
            (await BookAsync(court, promotion.Id)).Call));

        foreach (var unexpected in calls.Where(c => c.Status is not (HttpStatusCode.Created or HttpStatusCode.BadRequest)))
            await AssertStatusAsync(HttpStatusCode.Created, unexpected);
        var confirmed = calls.Count(c => c.Status == HttpStatusCode.Created);
        Assert.InRange(confirmed, 1, 2);
        Assert.All(calls.Where(c => c.Status == HttpStatusCode.BadRequest),
            c => Assert.Equal("Mã khuyến mãi đã hết lượt sử dụng", c.Message));
        Assert.Equal(confirmed, await ReadUsedCountAsync(promotion.Id));

        var (afterwards, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id);
        await AssertStatusAsync(HttpStatusCode.BadRequest, afterwards);
        Assert.Equal("Mã khuyến mãi đã hết lượt sử dụng", afterwards.Message);
    }

    [Fact]
    [Trait("Catalog", "P6-18")]
    public async Task CustomerWhoseOrderWasRejected_CanUseThePerCustomerPromotionAgain()
    {
        // P6-18: đơn bị TADSS từ chối không chiếm lượt của khách; đơn đang chờ thanh toán thì có
        var tadss = await GetTadssAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.FIXED, 10_000m, usagePerUserLimit: 1);
        var customer = await SeedUserAsync();
        using (await UseTadssAsync())
        {
            tadss.RejectAnyBooking();
            var (rejected, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id, customer);
            Assert.Equal("BOOKING_REJECTED", rejected.Code);
        }

        var (again, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id, customer);
        await AssertStatusAsync(HttpStatusCode.Created, again);
        Assert.Equal("PENDING", again.DataString("status"));

        var (third, _) = await BookAsync(await SeedCourtAsync(200_000m), promotion.Id, customer);
        await AssertStatusAsync(HttpStatusCode.BadRequest, third);
        Assert.Equal("Bạn đã sử dụng hết lượt áp dụng mã khuyến mãi này", third.Message);
    }
}
