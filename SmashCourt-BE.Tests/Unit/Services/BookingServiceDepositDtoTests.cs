using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// BE-UT-07 (gói 6A-2c Task 2): trường cọc, hạn duyệt, lý do duyệt và lý do hủy trên <see cref="BookingDto"/> qua đúng các hàm service mà
/// endpoint gọi. Lý do cần duyệt chỉ hiện ở endpoint nhân viên (Q6).
/// </summary>
[TestCategory(TestCategories.Booking)]
[Trait("Catalog", "BE-UT-07")]
public class BookingServiceDepositDtoTests
{
    private static readonly DateTime ApprovalExpiresAt = new(2026, 11, 18, 2, 45, 0, DateTimeKind.Utc);
    private static readonly List<string> Reasons = ["Khách có 3 lần không đến (ngưỡng 2)."];

    private static Booking Booking(
        BookingStatus status,
        decimal finalTotal = 228_000m,
        decimal? rate = null,
        decimal? amount = null,
        DepositSource? source = null,
        BookingSource bookingSource = BookingSource.ONLINE,
        bool wasPendingApproval = false,
        BookingCancelReason? cancelReason = null,
        Guid? customerId = null)
    {
        var booking = TestDataFactory.CreateBooking(customerId ?? Guid.NewGuid(), status: status);
        booking.Source = bookingSource;
        booking.CancelReason = cancelReason;
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: finalTotal);
        booking.Invoice.DepositRate = rate;
        booking.Invoice.DepositAmount = amount;
        booking.Invoice.DepositSource = source;
        if (wasPendingApproval)
        {
            booking.ApprovalExpiresAt = ApprovalExpiresAt;
            booking.ApprovalReasonCode = "NO_SHOW_HISTORY_REVIEW";
            booking.ApprovalReasons = [.. Reasons];
        }
        return booking;
    }

    private static async Task<BookingDto> StaffGetByIdAsync(Booking booking)
    {
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        return await builder.Build().GetByIdAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());
    }

    // ───────────────────────── Bốn trường cọc theo trạng thái ─────────────────────────

    [Fact]
    public async Task ManagerApprovedAndPaid_MatchesTheApiDocExample()
    {
        // DEPOSIT_APPROVAL_API 2.2: duyệt 30 %, đã cọc 68.400 trên 228.000
        var booking = Booking(BookingStatus.PAID_ONLINE, rate: 0.3m, amount: 68_400m, source: DepositSource.MANAGER, wasPendingApproval: true);

        var dto = await StaffGetByIdAsync(booking);

        Assert.Equal(0.3m, dto.DepositRate);
        Assert.Equal(68_400m, dto.DepositAmount);
        Assert.Equal(159_600m, dto.RemainingAmount);
        Assert.Equal("MANAGER", dto.DepositSource);
        Assert.Equal(ApprovalExpiresAt, dto.ApprovalExpiresAt);
        Assert.Equal("NO_SHOW_HISTORY_REVIEW", dto.ApprovalReasonCode);
        Assert.Equal(Reasons, dto.ApprovalReasons);
        Assert.Null(dto.CancelReason);
    }

    [Fact]
    public async Task PendingApproval_HasNoDepositFieldsAndNoPaymentDeadline_ButShowsTheApprovalDeadline()
    {
        var booking = Booking(BookingStatus.PENDING_APPROVAL, wasPendingApproval: true);
        booking.Invoice!.ExpiresAt = null;

        var dto = await StaffGetByIdAsync(booking);

        Assert.Equal("PENDING_APPROVAL", dto.Status);
        Assert.Null(dto.DepositRate);
        Assert.Null(dto.DepositAmount);
        Assert.Null(dto.RemainingAmount);
        Assert.Null(dto.DepositSource);
        Assert.Null(dto.ExpiresAt);
        Assert.Equal(ApprovalExpiresAt, dto.ApprovalExpiresAt);
    }

    [Theory]
    [InlineData(DepositSource.TADSS)]
    [InlineData(DepositSource.FULL_PAYMENT_FALLBACK)]
    public async Task BranchAPrime_IsZeroDepositAndTheWholeTotalRemains(DepositSource source)
    {
        var booking = Booking(BookingStatus.CONFIRMED, finalTotal: 3_000m, rate: 0m, amount: 0m, source: source);

        var dto = await StaffGetByIdAsync(booking);

        Assert.Equal(0m, dto.DepositRate);
        Assert.Equal(0m, dto.DepositAmount);
        Assert.Equal(3_000m, dto.RemainingAmount);
        Assert.Equal(source.ToString(), dto.DepositSource);
    }

    [Fact]
    public async Task FullPaymentFallback_IsRateOneAmountFinalTotal()
    {
        var booking = Booking(BookingStatus.PENDING, rate: 1m, amount: 228_000m, source: DepositSource.FULL_PAYMENT_FALLBACK);

        var dto = await StaffGetByIdAsync(booking);

        Assert.Equal((1m, 228_000m, 0m, "FULL_PAYMENT_FALLBACK"), (dto.DepositRate, dto.DepositAmount, dto.RemainingAmount, dto.DepositSource));
    }

    [Theory]
    [InlineData(BookingSource.WALK_IN, BookingStatus.CONFIRMED)]
    [InlineData(BookingSource.ONLINE, BookingStatus.PAID_ONLINE)] // đơn online tạo trước Phase 6
    public async Task WalkInAndPrePhase6Bookings_HaveNoDepositFields(BookingSource bookingSource, BookingStatus status)
    {
        var booking = Booking(status, bookingSource: bookingSource);

        var dto = await StaffGetByIdAsync(booking);

        Assert.Null(dto.DepositRate);
        Assert.Null(dto.DepositAmount);
        Assert.Null(dto.RemainingAmount);
        Assert.Null(dto.DepositSource);
        Assert.Null(dto.ApprovalExpiresAt);
        Assert.Null(dto.ApprovalReasonCode);
        Assert.Null(dto.ApprovalReasons);
    }

    // ───────────────────────── Lý do hủy ─────────────────────────

    [Theory]
    [InlineData(BookingCancelReason.POLICY_REJECTED)]
    [InlineData(BookingCancelReason.PAYMENT_EXPIRED)]
    [InlineData(BookingCancelReason.APPROVAL_EXPIRED)]
    [InlineData(BookingCancelReason.APPROVAL_REJECTED)]
    [InlineData(BookingCancelReason.CUSTOMER_CANCELLED)]
    [InlineData(BookingCancelReason.STAFF_CANCELLED)]
    [InlineData(BookingCancelReason.SYSTEM_CLEANUP)]
    public async Task CancelledBooking_ShowsItsCancelReason(BookingCancelReason reason)
    {
        var dto = await StaffGetByIdAsync(Booking(BookingStatus.CANCELLED, cancelReason: reason));

        Assert.Equal(reason.ToString(), dto.CancelReason);
    }

    [Fact]
    public async Task RejectedByPolicy_KeepsNoDepositFields()
    {
        // Nhánh c: hủy trước khi có mức cọc
        var dto = await StaffGetByIdAsync(Booking(BookingStatus.CANCELLED, cancelReason: BookingCancelReason.POLICY_REJECTED));

        Assert.Null(dto.DepositSource);
        Assert.Equal("POLICY_REJECTED", dto.CancelReason);
    }

    // ───────────────────────── Lý do cần duyệt: nhân viên thấy, khách không (Q6) ─────────────────────────

    [Fact]
    public async Task StaffList_ShowsApprovalReasons()
    {
        var booking = Booking(BookingStatus.PENDING_APPROVAL, wasPendingApproval: true);
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetAllAsync(It.IsAny<BookingListQuery>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new PagedResult<Booking> { Items = [booking], Page = 1, PageSize = 10, TotalItems = 1 });

        var page = await builder.Build().GetAllAsync(new BookingListQuery(), Guid.NewGuid(), UserRole.STAFF.ToString());

        var dto = Assert.Single(page.Items);
        Assert.Equal("NO_SHOW_HISTORY_REVIEW", dto.ApprovalReasonCode);
        Assert.Equal(Reasons, dto.ApprovalReasons);
    }

    [Theory]
    [InlineData(nameof(UserRole.OWNER))]
    [InlineData(nameof(UserRole.BRANCH_MANAGER))]
    [InlineData(nameof(UserRole.STAFF))]
    public async Task StaffDetails_ShowApprovalReasons(string role)
    {
        var booking = Booking(BookingStatus.PENDING_APPROVAL, wasPendingApproval: true);
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.UserBranchRepository.Setup(x => x.IsUserInBranchAsync(It.IsAny<Guid>(), booking.BranchId)).ReturnsAsync(true);

        var dto = await builder.Build().GetDetailsByIdAsync(booking.Id, Guid.NewGuid(), role);

        Assert.Equal("NO_SHOW_HISTORY_REVIEW", dto.ApprovalReasonCode);
        Assert.Equal(Reasons, dto.ApprovalReasons);
    }

    [Fact]
    public async Task CustomerDetails_HideApprovalReasons_ButKeepEverythingElse()
    {
        var customerId = Guid.NewGuid();
        var booking = Booking(BookingStatus.PAID_ONLINE, rate: 0.3m, amount: 68_400m, source: DepositSource.MANAGER,
            wasPendingApproval: true, customerId: customerId);
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        var dto = await builder.Build().GetDetailsByIdAsync(booking.Id, customerId, UserRole.CUSTOMER.ToString());

        Assert.Null(dto.ApprovalReasonCode);
        Assert.Null(dto.ApprovalReasons);
        Assert.Equal(ApprovalExpiresAt, dto.ApprovalExpiresAt);
        Assert.Equal(68_400m, dto.DepositAmount);
        Assert.Equal(159_600m, dto.RemainingAmount);
        Assert.Equal("MANAGER", dto.DepositSource);
    }

    [Fact]
    public async Task CustomerHistory_HidesApprovalReasons()
    {
        var customerId = Guid.NewGuid();
        var booking = Booking(BookingStatus.PENDING_APPROVAL, wasPendingApproval: true, customerId: customerId);
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByCustomerIdAsync(customerId, It.IsAny<BookingListQuery>()))
            .ReturnsAsync(new PagedResult<Booking> { Items = [booking], Page = 1, PageSize = 10, TotalItems = 1 });

        var page = await builder.Build().GetMyBookingsAsync(customerId, new BookingListQuery());

        var dto = Assert.Single(page.Items);
        Assert.Null(dto.ApprovalReasonCode);
        Assert.Null(dto.ApprovalReasons);
        Assert.Equal(ApprovalExpiresAt, dto.ApprovalExpiresAt);
    }

    [Fact]
    public async Task GetById_WithCustomerRole_HidesApprovalReasons()
    {
        // Endpoint này chỉ cho nhân viên, nhưng hàm service vẫn nhận role khách: không được lộ lý do
        var customerId = Guid.NewGuid();
        var booking = Booking(BookingStatus.PENDING_APPROVAL, wasPendingApproval: true, customerId: customerId);
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        var dto = await builder.Build().GetByIdAsync(booking.Id, customerId, UserRole.CUSTOMER.ToString());

        Assert.Null(dto.ApprovalReasonCode);
        Assert.Null(dto.ApprovalReasons);
    }

    [Fact]
    public async Task ApprovalReasons_AreCopied_NotShared()
    {
        var booking = Booking(BookingStatus.PENDING_APPROVAL, wasPendingApproval: true);

        var dto = await StaffGetByIdAsync(booking);
        dto.ApprovalReasons?.Add("sửa ở DTO");

        Assert.Equal(Reasons, booking.ApprovalReasons);
    }
}
