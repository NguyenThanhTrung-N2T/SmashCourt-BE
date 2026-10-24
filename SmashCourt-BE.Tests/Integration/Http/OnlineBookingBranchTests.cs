using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-2c Task 4: năm nhánh của <c>POST /api/bookings/online</c> qua HTTP thật, TADSS là WireMock (BE-IT-02, 03, 04 phần cờ tắt, 16, 17,
/// 19, 24; A9-H; E-01…E-03). Giá mặc định 200.000 đ/giờ, đặt 1 giờ.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed partial class OnlineBookingBranchTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : OnlineBookingTestBase(database, host)
{
    [GeneratedRegex(@"/booking/cancel\?token=([A-Za-z0-9_\-]+)")]
    private static partial Regex CancelLinkPattern();

    private static void AssertSchemaValid(WireMock.Logging.ILogEntry entry)
    {
        var result = TadssContractFiles.Evaluate(TadssContractFiles.DecisionRequest, entry.RequestMessage?.Body ?? "");
        Assert.True(result.IsValid, TadssContractFiles.Describe(result));
    }

    // ───────────────────────── (a) TADSS APPROVED, cọc ≥ mức tối thiểu ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-02")]
    public async Task BranchA_TadssDeposit_ChargesTheDepositAndStoresEverything()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        tadss.ApproveAnyBooking(0.5m, 100_000m);

        var (call, customer) = await BookAsync(court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("Đặt sân online thành công", call.Message);
        Assert.Equal("PENDING", call.DataString("status"));
        Assert.Equal((0.5m, 100_000m, 100_000m),
            (call.DataDecimal("depositRate"), call.DataDecimal("depositAmount"), call.DataDecimal("remainingAmount")));
        Assert.Equal("TADSS", call.DataString("depositSource"));
        Assert.Equal(100_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        Assert.NotNull(call.DataString("expiresAt"));
        Assert.False(call.Data.TryGetProperty("decisionId", out var leaked), leaked.ToString());

        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(customer.Id, booking.CustomerId);
        Assert.Null(booking.GuestEmail);
        Assert.NotNull(booking.TadssDecisionId);
        var invoice = Assert.IsType<Invoice>(booking.Invoice);
        Assert.Equal((0.5m, 100_000m, DepositSource.TADSS), (invoice.DepositRate, invoice.DepositAmount, invoice.DepositSource));
        Assert.Equal(PaymentTiming.PREPAID, invoice.PaymentTiming);
        Assert.Equal(booking.CreatedAt.AddMinutes(10), invoice.ExpiresAt);
        Assert.Equal(booking.CreatedAt.AddMinutes(15), invoice.HoldDeadline);
        var payment = Assert.Single(invoice.Payments);
        Assert.Equal((100_000m, PaymentTxStatus.PENDING), (payment.Amount, payment.Status));
        var slotLock = Assert.Single(await ReadLocksAsync(call.BookingId));
        Assert.Equal(invoice.ExpiresAt, slotLock.ExpiresAt);

        // Request gửi TADSS hợp lệ schema v2 và mang đúng booking
        var request = Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
        AssertSchemaValid(request);
        using var body = JsonDocument.Parse(request.RequestMessage?.Body ?? "{}");
        Assert.Equal(call.BookingId, body.RootElement.GetProperty("reservationId").GetGuid());
        Assert.Equal(200_000m, body.RootElement.GetProperty("totalPrice").GetDecimal());
    }

    [Theory]
    [Trait("Catalog", "BE-IT-02")]
    [InlineData("decision-result.approved-fallback.json", null)]
    [InlineData("decision-result.approved.json", "APPROVAL_WINDOW_UNAVAILABLE")]
    public async Task BranchA_RuntimeFallbackAndApprovalWindowUnavailable_AreTadssDecisions(string sample, string? reasonCode)
    {
        // Fallback của runtime TADSS và APPROVAL_WINDOW_UNAVAILABLE vẫn là quyết định TADSS: nguồn TADSS, có decisionId
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        tadss.ReplyForAnyBooking(sample, json =>
        {
            json["deposit"] = new System.Text.Json.Nodes.JsonObject { ["rate"] = 1m, ["amount"] = 200_000m, ["currency"] = "VND" };
            if (reasonCode != null)
                json["reasonCode"] = reasonCode;
        });

        var (call, _) = await BookAsync(court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("TADSS", call.DataString("depositSource"));
        Assert.Equal(200_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        Assert.NotNull((await RequireBookingAsync(call.BookingId)).TadssDecisionId);
    }

    // ───────────────────────── (b) NEED_APPROVAL ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-03")]
    public async Task BranchB_HoldsTheSlotUntilTheApprovalDeadline_WithoutPayment()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        var approvalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20);
        tadss.RequireApprovalForAnyBooking(approvalExpiresAt);
        var date = FutureDate();

        var (call, customer) = await BookAsync(court, date: date);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("Đơn đặt sân đang chờ quản lý chi nhánh duyệt mức cọc", call.Message);
        Assert.Equal("PENDING_APPROVAL", call.DataString("status"));
        foreach (var key in new[] { "paymentUrl", "expiresAt", "depositRate", "depositAmount", "remainingAmount", "depositSource" })
            Assert.Equal(JsonValueKind.Null, call.Data.GetProperty(key).ValueKind);
        Assert.NotNull(call.DataString("approvalExpiresAt"));

        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.PENDING_APPROVAL, booking.Status);
        Assert.NotNull(booking.TadssDecisionId);
        Assert.Equal("HIGH_VALUE_REVIEW", booking.ApprovalReasonCode);
        Assert.NotEmpty(booking.ApprovalReasons ?? []);
        var invoice = Assert.IsType<Invoice>(booking.Invoice);
        Assert.Null(invoice.ExpiresAt);
        Assert.Null(invoice.DepositSource);
        Assert.Empty(invoice.Payments);
        var slotLock = Assert.Single(await ReadLocksAsync(call.BookingId));
        Assert.Equal(booking.ApprovalExpiresAt, slotLock.ExpiresAt);
        Assert.InRange(slotLock.ExpiresAt, approvalExpiresAt.UtcDateTime.AddSeconds(-1), approvalExpiresAt.UtcDateTime.AddSeconds(1));

        // Lưới giờ thấy ô bị giữ
        var client = await CustomerClientAsync(customer);
        var grid = await client.GetFromJsonAsync<JsonElement>(
            $"/api/branches/{court.BranchId}/courts/{court.CourtId}/time-grid?date={date:yyyy-MM-dd}");
        var cell = grid.GetProperty("data").EnumerateArray().Single(s => s.GetProperty("startTime").GetString() == "10:00:00");
        Assert.Equal("LOCKED", cell.GetProperty("status").GetString());

        // Không lách duyệt bằng thanh toán lại
        using var retry = await client.PostAsync($"/api/me/bookings/{call.BookingId}/retry-payment", null);
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        Assert.Empty((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []);
    }

    // ───────────────────────── (c) REJECTED ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-17")]
    public async Task BranchC_Rejects400_ReleasesTheSlot_AndRecordsTheDecision()
    {
        var tadss = await GetTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        BookingCall call;
        using (await UseTadssAsync())
        {
            tadss.RejectAnyBooking();
            (call, _) = await BookAsync(court);
        }

        await AssertStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("BOOKING_REJECTED", call.Code);
        Assert.Equal(
            "Rất tiếc, đơn đặt sân này không được chấp nhận. Vui lòng chọn khung giờ khác hoặc liên hệ chi nhánh.", call.Message);
        Assert.Equal(JsonValueKind.Null, call.Data.ValueKind);
        // Lý do của TADSS không lộ cho khách (Q6)
        Assert.DoesNotContain("reasons", call.Envelope.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REPEATED_PAYMENT_EXPIRY", call.Envelope.GetRawText());

        var booking = await FindBookingOnCourtAsync(court.CourtId) ?? throw new InvalidOperationException("Không thấy booking");
        Assert.Single(await tadss.WaitForEvaluationsAsync(booking.Id, 1));
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(BookingCancelReason.POLICY_REJECTED, booking.CancelReason);
        Assert.Equal(CancelSourceEnum.SYSTEM, booking.CancelSource);
        Assert.NotNull(booking.CancelledAt);
        Assert.NotNull(booking.TadssDecisionId);
        Assert.All(booking.BookingCourts, bc => Assert.False(bc.IsActive));
        Assert.Equal(InvoicePaymentStatus.EXPIRED, booking.Invoice?.PaymentStatus);
        Assert.Empty(booking.Invoice?.Payments ?? []);
        Assert.Empty(await ReadLocksAsync(booking.Id));
        Assert.False(await HasSlotInterestAsync(court.CourtId));

        // Slot trống lại: khách khác đặt cùng giờ được (cờ tắt)
        var (again, _) = await BookAsync(court);
        await AssertStatusAsync(HttpStatusCode.Created, again);
    }

    // ───────────────────────── (d) cờ tắt ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task BranchD_FlagOff_ChargesTheFullTotalWithoutCallingTadss()
    {
        var tadss = await GetTadssAsync();
        var court = await SeedCourtAsync(200_000m);

        var (call, _) = await BookAsync(court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("PENDING", call.DataString("status"));
        Assert.Equal((1m, 200_000m, 0m),
            (call.DataDecimal("depositRate"), call.DataDecimal("depositAmount"), call.DataDecimal("remainingAmount")));
        Assert.Equal("FULL_PAYMENT_FALLBACK", call.DataString("depositSource"));
        Assert.Equal(200_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Null(booking.TadssDecisionId);
        Assert.Equal(200_000m, Assert.Single(booking.Invoice?.Payments ?? []).Amount);
        Assert.Empty(await tadss.WaitForEvaluationsAsync(call.BookingId, 0));
    }

    [Fact]
    public async Task MultiCourtOrder_HoldsOneLockPerCourt_AndChargesBoth()
    {
        var court = await SeedCourtAsync(200_000m);
        var second = await SeedSecondCourtAsync(court);
        var client = await CustomerClientAsync(await SeedUserAsync());

        var call = await PostOnlineAsync(client, OnlineRequest([court.CourtId, second], FutureDate()));

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(400_000m, call.DataDecimal("finalTotal"));
        Assert.Equal(400_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        Assert.Equal(new[] { court.CourtId, second }.Order(), (await ReadLocksAsync(call.BookingId)).Select(l => l.CourtId).Order());
    }

    // ───────────────────────── BE-IT-16: đặt tại quầy không gọi TADSS ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-16")]
    public async Task WalkIn_WithFlagOn_NeverCallsTadss_AndHasNoDepositFields()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        var court = await SeedCourtAsync(200_000m);
        var owner = await SeedUserAsync(UserRole.OWNER);
        var client = await CustomerClientAsync(owner);

        using var response = await client.PostAsJsonAsync("/api/bookings/walk-in", new
        {
            bookingDate = FutureDate().ToString("yyyy-MM-dd"),
            courts = new[] { new { courtId = court.CourtId, startTime = "10:00:00", endTime = "11:00:00" } },
            guestName = "Khách tại quầy",
            guestPhone = "0900000000"
        });

        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, text);
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");
        foreach (var key in new[] { "depositRate", "depositAmount", "remainingAmount", "depositSource", "approvalExpiresAt", "cancelReason" })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(key).ValueKind);
        Assert.Equal("Khách tại quầy", data.GetProperty("guestName").GetString());
        await Task.Delay(300);
        Assert.Empty(tadss.EvaluationsMentioning(court.CourtId));
    }

    // ───────────────────────── BE-IT-19: đổi cờ lúc chạy ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-19")]
    public async Task FlagChangesAtRuntime_ApplyToTheNextBooking_WithoutRestart()
    {
        var tadss = await GetTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        var factory = await GetFactoryAsync();

        var (off, _) = await BookAsync(await SeedCourtAsync());
        Assert.Equal("FULL_PAYMENT_FALLBACK", off.DataString("depositSource"));

        var onCourt = await SeedCourtAsync();
        var switchedAt = DateTime.UtcNow;
        factory.SetSetting(EnableKey, "true");
        try
        {
            var (on, _) = await BookAsync(onCourt);
            Assert.True(DateTime.UtcNow - switchedAt < TimeSpan.FromSeconds(2));
            Assert.Equal("TADSS", on.DataString("depositSource"));
            Assert.Single(await tadss.WaitForEvaluationsAsync(on.BookingId, 1));
        }
        finally
        {
            factory.SetSetting(EnableKey, null);
        }

        var (offAgain, _) = await BookAsync(await SeedCourtAsync());
        Assert.Equal("FULL_PAYMENT_FALLBACK", offAgain.DataString("depositSource"));
        Assert.Empty(await tadss.WaitForEvaluationsAsync(offAgain.BookingId, 0));
    }

    // ───────────────────────── BE-IT-24: quyền và đơn dưới mức tối thiểu ─────────────────────────

    public static TheoryData<UserRole?, HttpStatusCode> NonCustomers => new()
    {
        { null, HttpStatusCode.Unauthorized },
        { UserRole.STAFF, HttpStatusCode.Forbidden },
        { UserRole.BRANCH_MANAGER, HttpStatusCode.Forbidden },
        { UserRole.OWNER, HttpStatusCode.Forbidden },
    };

    [Theory]
    [Trait("Catalog", "BE-IT-24")]
    [MemberData(nameof(NonCustomers))]
    public async Task OnlyCustomersCanBookOnline_OthersNeverReachTadss(UserRole? role, HttpStatusCode expected)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        var court = await SeedCourtAsync();
        var client = role is { } r ? await CustomerClientAsync(await SeedUserAsync(r)) : await CreateAnonymousClientAsync("203.0.113.10");

        var call = await PostOnlineAsync(client, OnlineRequest([court.CourtId], FutureDate()));

        Assert.Equal(expected, call.Status);
        Assert.Null(await FindBookingOnCourtAsync(court.CourtId));
        await Task.Delay(300);
        Assert.Empty(tadss.EvaluationsMentioning(court.CourtId));
    }

    [Theory]
    [Trait("Catalog", "BE-IT-24")]
    [InlineData(4_999)] // dưới mức tối thiểu 5.000 đ
    [InlineData(0)]
    public async Task ApprovedBelowTheMinimum_ConfirmsWithoutOnlinePayment(int amount)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(Math.Round(amount / 200_000m, 4), amount);
        var court = await SeedCourtAsync(200_000m);

        var (call, _) = await BookAsync(court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("Đặt sân thành công, vui lòng thanh toán tại quầy", call.Message);
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal((0m, 0m, 200_000m),
            (call.DataDecimal("depositRate"), call.DataDecimal("depositAmount"), call.DataDecimal("remainingAmount")));
        Assert.Equal("TADSS", call.DataString("depositSource"));
        Assert.Equal(JsonValueKind.Null, call.Data.GetProperty("paymentUrl").ValueKind);
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        Assert.NotNull(booking.TadssDecisionId);
        Assert.Equal(PaymentTiming.POSTPAID, booking.Invoice?.PaymentTiming);
        Assert.Null(booking.Invoice?.ExpiresAt);
        Assert.Empty(booking.Invoice?.Payments ?? []);
        Assert.Empty(await ReadLocksAsync(call.BookingId));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-24")]
    [Trait("Catalog", "Z-16")]
    public async Task FreeOrderApprovedByTadss_IsConfirmedWithoutPayment_AndItsRequestIsSchemaValid()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0m, 0m);
        var court = await SeedCourtAsync(200_000m);
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 100m);

        var (call, _) = await BookAsync(court, promotion.Id);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal(0m, call.DataDecimal("finalTotal"));
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Empty((await RequireBookingAsync(call.BookingId)).Invoice?.Payments ?? []);
        // Đơn 0 đ vẫn hỏi TADSS (strategy xét lịch sử khách, không chỉ giá); request 0 đ hợp lệ schema
        var request = Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
        AssertSchemaValid(request);
        using var body = JsonDocument.Parse(request.RequestMessage?.Body ?? "{}");
        Assert.Equal(0m, body.RootElement.GetProperty("totalPrice").GetDecimal());
    }

    // ───────────────────────── A9-H: khung giờ đã bắt đầu ─────────────────────────

    [Fact]
    [Trait("Catalog", "A9-H")]
    public async Task SlotThatAlreadyStartedToday_IsRejectedBeforeAnythingIsWritten()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        var court = await SeedCourtAsync();
        var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, DateTimeHelper.VNTimezone);
        var start = new TimeSpan(vnNow.Hour, 0, 0); // giờ hiện tại đã bắt đầu (≤ bây giờ)
        var end = vnNow.Hour == 23 ? new TimeSpan(23, 59, 0) : start.Add(TimeSpan.FromHours(1));
        var client = await CustomerClientAsync(await SeedUserAsync());

        var call = await PostOnlineAsync(client, new
        {
            bookingDate = DateOnly.FromDateTime(vnNow).ToString("yyyy-MM-dd"),
            courts = new[] { new { courtId = court.CourtId, startTime = start.ToString(@"hh\:mm\:ss"), endTime = end.ToString(@"hh\:mm\:ss") } }
        });

        await AssertStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("BAD_REQUEST", call.Code);
        Assert.Equal("Khung giờ đã bắt đầu hoặc đã qua, vui lòng chọn khung giờ khác", call.Message);
        Assert.Null(await FindBookingOnCourtAsync(court.CourtId));
        Assert.False(await WithDbAsync(db => db.SlotLocks.AnyAsync(l => l.CourtId == court.CourtId)));
        await Task.Delay(300);
        Assert.Empty(tadss.EvaluationsMentioning(court.CourtId));
    }

    // ───────────────────────── E-01…E-03: email của đơn a′ ─────────────────────────

    /// <summary>Khung 1 giờ bắt đầu sau khoảng 3–4 giờ nữa (giờ VN, không vắt qua nửa đêm) để hạn link hủy là giờ bắt đầu chơi.</summary>
    private static (DateOnly Date, int Hour) SlotInAboutThreeHours()
    {
        var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddHours(3), DateTimeHelper.VNTimezone);
        var hour = start.Hour + 1;
        var date = DateOnly.FromDateTime(start);
        return hour > 22 ? (date.AddDays(1), 6) : (date, hour);
    }

    [Fact]
    [Trait("Catalog", "E-01")]
    public async Task BranchAPrime_SendsOneEmailWhoseCancelLinkWorks_AndExpiresAtPlayStart()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.01m, 2_000m);
        var court = await SeedCourtAsync(200_000m);
        var (date, hour) = SlotInAboutThreeHours();
        var factory = await GetFactoryAsync();

        var (call, customer) = await BookAsync(court, date: date, hour: hour);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("CONFIRMED", call.DataString("status"));
        var email = Assert.Single(factory.Emails.To(customer.Email));
        var token = CancelLinkPattern().Match(email.Body).Groups[1].Value;
        Assert.NotEmpty(token);
        Assert.Contains("http://localhost:3000/booking/cancel?token=", email.Body);

        // Khứ hồi: token trong link mở được trang hủy
        using var info = await (await CreateAnonymousClientAsync()).GetAsync($"/api/bookings/cancel/{token}");
        var infoBody = await info.Content.ReadAsStringAsync();
        Assert.True(info.StatusCode == HttpStatusCode.OK, infoBody);
        Assert.Equal(call.BookingId, JsonDocument.Parse(infoBody).RootElement.GetProperty("data").GetProperty("bookingId").GetGuid());

        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(CancelTokenHelper.Hash(token), booking.CancelTokenHash);
        Assert.Equal(DateTimeHelper.ToUtcFromVietnam(date, new TimeOnly(hour, 0)), booking.CancelTokenExpiresAt);
    }

    [Fact]
    [Trait("Catalog", "E-02")]
    public async Task BranchAPrime_WhenSmtpFails_StillConfirmsAndKeepsTheToken()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(0.01m, 2_000m);
        var court = await SeedCourtAsync(200_000m);
        var factory = await GetFactoryAsync();
        factory.Emails.FailOnSend = true;
        try
        {
            var (call, customer) = await BookAsync(court);

            await AssertStatusAsync(HttpStatusCode.Created, call);
            Assert.Equal("CONFIRMED", call.DataString("status"));
            Assert.Empty(factory.Emails.To(customer.Email));
            var booking = await RequireBookingAsync(call.BookingId);
            Assert.NotNull(booking.CancelTokenHash);
            Assert.NotNull(booking.CancelTokenExpiresAt);
        }
        finally
        {
            factory.Emails.FailOnSend = false;
        }
    }

    [Theory]
    [Trait("Catalog", "E-03")]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    public async Task OtherBranches_SendNoConfirmationEmailAtBookingTime(string branch)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        switch (branch)
        {
            case "a": tadss.ApproveAnyBooking(0.5m, 100_000m); break;
            case "b": tadss.RequireApprovalForAnyBooking(DateTimeOffset.UtcNow.AddMinutes(20)); break;
            case "c": tadss.RejectAnyBooking(); break;
            default: tadss.Reply(503, null); break; // d: TADSS lỗi ⇒ dự phòng 100 %
        }
        var court = await SeedCourtAsync(200_000m);
        var factory = await GetFactoryAsync();

        var (call, customer) = await BookAsync(court);

        await AssertStatusAsync(branch == "c" ? HttpStatusCode.BadRequest : HttpStatusCode.Created, call);
        Assert.Empty(factory.Emails.To(customer.Email));
        var booking = await FindBookingOnCourtAsync(court.CourtId) ?? throw new InvalidOperationException("Không thấy booking");
        Assert.Null(booking.CancelTokenHash);
    }
}
