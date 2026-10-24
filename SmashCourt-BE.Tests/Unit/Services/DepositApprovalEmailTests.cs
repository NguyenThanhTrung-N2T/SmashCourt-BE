using SmashCourt_BE.Factories;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Email luồng duyệt cọc (spec 7.13, chốt 24/10): mẫu trong <c>Templates/</c>; giờ hiển thị là giờ VN <c>HH:mm dd/MM/yyyy</c>;
/// không bao giờ chứa link VNPay (P6-10, N-15); email từ chối không có lý do của Quản lý.
/// </summary>
[Trait("Category", TestCategories.Email)]
[Trait("Catalog", "BE-IT-07")]
public class DepositApprovalEmailTests
{
    private const string FrontendBaseUrl = "https://fe.test";

    private readonly RecordingEmailService _email = new(TestConfigurationFactory.Create());

    private static Booking ApprovedBooking(decimal finalTotal = 228_000m, decimal? deposit = 68_400m, DateTime? expiresAt = null)
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PENDING, bookingDate: new DateOnly(2026, 11, 20));
        booking.BookingCode = "BK-20261118-000123";
        booking.Customer = new User { Id = booking.CustomerId!.Value, Email = "khach@test.local", FullName = "Nguyễn Văn An" };
        booking.Branch = new Branch { Id = booking.BranchId, Name = "SmashCourt Thủ Đức", Address = "1 Võ Văn Ngân", Phone = "0281234567" };
        booking.BookingCourts.Add(new BookingCourt
        {
            CourtId = Guid.NewGuid(), Court = new Court { Name = "Sân 3" }, Date = new DateOnly(2026, 11, 20),
            StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(20, 0)
        });
        booking.ApprovalExpiresAt = new DateTime(2026, 11, 18, 2, 45, 0, DateTimeKind.Utc); // 09:45 giờ VN
        booking.Invoice = new Invoice
        {
            FinalTotal = finalTotal,
            DepositAmount = deposit,
            DepositRate = deposit is null ? null : 0.3m,
            DepositSource = deposit is null ? null : DepositSource.MANAGER,
            ExpiresAt = expiresAt ?? new DateTime(2026, 11, 18, 2, 50, 0, DateTimeKind.Utc) // 09:50 giờ VN
        };
        return booking;
    }

    // ── Model ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildApproval_FormatsMoneyAndVietnamTimes()
    {
        var model = BookingEmailFactory.BuildApproval(ApprovedBooking(), FrontendBaseUrl);

        Assert.Equal("khach@test.local", model.Email);
        Assert.Equal("Nguyễn Văn An", model.Name);
        Assert.Equal("BK-20261118-000123", model.BookingCode);
        Assert.Equal("Sân 3", Assert.Single(model.CourtNames));
        Assert.Equal("20/11/2026", model.BookingDate);
        Assert.Equal("18:00", model.StartTime);
        Assert.Equal("20:00", model.EndTime);
        Assert.Equal(228_000m.ToString("N0") + " VND", model.TotalAmount);
        Assert.Equal(68_400m.ToString("N0") + " VND", model.DepositAmount);
        Assert.Equal(159_600m.ToString("N0") + " VND", model.RemainingAmount);
        Assert.Equal("09:50 18/11/2026", model.PayBefore);
        Assert.Equal("09:45 18/11/2026", model.ApprovalExpiresAt);
        Assert.Equal("https://fe.test/bookings", model.BookingsUrl);
        Assert.Equal("0281234567", model.BranchPhone);
    }

    [Fact]
    public void BuildApproval_BeforeDepositIsKnown_LeavesMoneyAndDeadlineEmpty()
    {
        var model = BookingEmailFactory.BuildApproval(WithoutPaymentDeadline(ApprovedBooking(deposit: null)), FrontendBaseUrl);

        Assert.Null(model.DepositAmount);
        Assert.Null(model.RemainingAmount);
        Assert.Null(model.PayBefore);
        Assert.Equal("09:45 18/11/2026", model.ApprovalExpiresAt);
    }

    private static Booking WithoutPaymentDeadline(Booking booking)
    {
        booking.Invoice!.ExpiresAt = null;
        return booking;
    }

    [Fact]
    public void BuildApproval_WithoutCustomerEmail_Throws()
    {
        var booking = ApprovedBooking();
        booking.Customer!.Email = "";

        Assert.Throws<InvalidOperationException>(() => BookingEmailFactory.BuildApproval(booking, FrontendBaseUrl));
    }

    // ── Đã duyệt, cần trả cọc ─────────────────────────────────────────────

    [Fact]
    public async Task DepositApproved_ShowsDepositRemainingDeadlineAndHistoryLink_WithoutPaymentLink()
    {
        await _email.SendDepositApprovedAsync(BookingEmailFactory.BuildApproval(ApprovedBooking(), FrontendBaseUrl));

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("khach@test.local", sent.To);
        Assert.Contains("BK-20261118-000123", sent.Subject);
        Assert.Contains(68_400m.ToString("N0") + " VND", sent.Body);
        Assert.Contains(159_600m.ToString("N0") + " VND", sent.Body);
        Assert.Contains("09:50 18/11/2026", sent.Body);
        Assert.Contains("https://fe.test/bookings", sent.Body);
        AssertNoPaymentLinkAndNoPlaceholder(sent.Body);
    }

    // ── Bị từ chối ────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovalRejected_ShowsBookingAndBranchContact_WithoutManagerReason()
    {
        var booking = ApprovedBooking(deposit: null);
        booking.Status = BookingStatus.CANCELLED;
        booking.ApprovalResolutionNote = "Khách bỏ sân 3 lần, không tin tưởng";

        await _email.SendApprovalRejectedAsync(BookingEmailFactory.BuildApproval(booking, FrontendBaseUrl));

        var sent = Assert.Single(_email.Sent);
        Assert.Contains("BK-20261118-000123", sent.Body);
        Assert.Contains("20/11/2026", sent.Body);
        Assert.Contains("0281234567", sent.Body);
        Assert.DoesNotContain("bỏ sân", sent.Body);
        AssertNoPaymentLinkAndNoPlaceholder(sent.Body);
    }

    // ── Đang chờ duyệt ────────────────────────────────────────────────────

    [Fact]
    public async Task BookingPendingApproval_ShowsApprovalDeadline()
    {
        var booking = WithoutPaymentDeadline(ApprovedBooking(deposit: null));
        booking.Status = BookingStatus.PENDING_APPROVAL;

        await _email.SendBookingPendingApprovalAsync(BookingEmailFactory.BuildApproval(booking, FrontendBaseUrl));

        var sent = Assert.Single(_email.Sent);
        Assert.Contains("BK-20261118-000123", sent.Body);
        Assert.Contains("09:45 18/11/2026", sent.Body);
        Assert.Contains("https://fe.test/bookings", sent.Body);
        AssertNoPaymentLinkAndNoPlaceholder(sent.Body);
    }

    private static void AssertNoPaymentLinkAndNoPlaceholder(string body)
    {
        Assert.DoesNotContain("vnpay", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{", body);
        Assert.DoesNotContain("}}", body);
    }
}
