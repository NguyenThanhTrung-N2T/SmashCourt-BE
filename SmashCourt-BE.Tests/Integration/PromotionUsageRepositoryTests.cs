using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Khuyến mãi trên schema thật:
/// <list type="bullet">
/// <item>P6-18 (N-22): giới hạn lượt mỗi khách chỉ đếm booking không ở trạng thái hủy.</item>
/// <item>N-28: bản chụp khuyến mãi trên đơn nhận được mã số tiền cố định &gt; 100 đ; mã phần trăm vẫn ≤ 100.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Promotion")]
public sealed class PromotionUsageRepositoryTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<(Promotion Promotion, User Customer, Guid BranchId)> SeedAsync()
    {
        var branch = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var promotion = await Seeder.SeedPromotionAsync($"P{Guid.NewGuid():N}"[..12]);
        return (promotion, customer, branch.Branch.Id);
    }

    private async Task AddBookingWithPromotionAsync(Guid branchId, Guid customerId, Promotion promotion, BookingStatus status)
    {
        var booking = TestDataSeeder.NewBooking(branchId, customerId, status);
        if (BookingStatusTransition.GetCancelledStatuses().Contains(status))
            booking.CancelReason = BookingCancelReason.POLICY_REJECTED;
        if (status == BookingStatus.PENDING_APPROVAL)
        {
            // chk_bookings_pending_approval: đơn chờ duyệt phải có quyết định TADSS và hạn duyệt
            booking.TadssDecisionId = Guid.NewGuid();
            booking.ApprovalExpiresAt = DateTime.UtcNow.AddMinutes(20);
        }
        DbContext.Bookings.Add(booking);
        DbContext.BookingPromotions.Add(TestDataSeeder.NewBookingPromotion(booking.Id, promotion));
        await DbContext.SaveChangesAsync();
    }

    public static TheoryData<BookingStatus, int> CountByStatus()
    {
        var data = new TheoryData<BookingStatus, int>();
        foreach (var status in Enum.GetValues<BookingStatus>())
            data.Add(status, BookingStatusTransition.GetCancelledStatuses().Contains(status) ? 0 : 1);
        return data;
    }

    [Theory]
    [Trait("Catalog", "P6-18")]
    [MemberData(nameof(CountByStatus))]
    public async Task UserUsageCount_IgnoresCancelledBookings_ButCountsEveryOtherStatus(BookingStatus status, int expected)
    {
        // Đơn còn giữ lượt (chờ thanh toán, chờ duyệt) hay đã dùng (hoàn thành, không đến) đều tính; đơn hủy/hết hạn/bị từ chối trả lượt cho khách
        var (promotion, customer, branchId) = await SeedAsync();
        await AddBookingWithPromotionAsync(branchId, customer.Id, promotion, status);

        var count = await new PromotionRepository(DbContext).GetUserUsageCountAsync(promotion.Id, customer.Id);

        Assert.Equal(expected, count);
    }

    [Fact]
    [Trait("Catalog", "P6-18")]
    public async Task UserUsageCount_CountsOnlyThisCustomerAndThisPromotion()
    {
        var (promotion, customer, branchId) = await SeedAsync();
        var other = await Seeder.SeedCustomerAsync();
        var otherPromotion = await Seeder.SeedPromotionAsync($"Q{Guid.NewGuid():N}"[..12]);
        await AddBookingWithPromotionAsync(branchId, customer.Id, promotion, BookingStatus.CONFIRMED);
        await AddBookingWithPromotionAsync(branchId, customer.Id, promotion, BookingStatus.CANCELLED);
        await AddBookingWithPromotionAsync(branchId, customer.Id, promotion, BookingStatus.PAID_ONLINE);
        await AddBookingWithPromotionAsync(branchId, other.Id, promotion, BookingStatus.CONFIRMED);
        await AddBookingWithPromotionAsync(branchId, customer.Id, otherPromotion, BookingStatus.CONFIRMED);

        var count = await new PromotionRepository(DbContext).GetUserUsageCountAsync(promotion.Id, customer.Id);

        Assert.Equal(2, count);
    }

    // ───────────────────────── N-28 ─────────────────────────

    private async Task<DbUpdateException?> TrySaveSnapshotAsync(DiscountTypeEnum type, decimal value)
    {
        var (promotion, customer, branchId) = await SeedAsync();
        var booking = TestDataSeeder.NewBooking(branchId, customer.Id, BookingStatus.CONFIRMED);
        DbContext.Bookings.Add(booking);
        var snapshot = TestDataSeeder.NewBookingPromotion(booking.Id, promotion);
        snapshot.DiscountTypeSnapshot = type;
        snapshot.DiscountValueSnapshot = value;
        DbContext.BookingPromotions.Add(snapshot);
        try
        {
            await DbContext.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException ex)
        {
            DbContext.ChangeTracker.Clear();
            return ex;
        }
    }

    [Theory]
    [Trait("Catalog", "N-28")]
    [InlineData(DiscountTypeEnum.FIXED, 20_000)]
    [InlineData(DiscountTypeEnum.FIXED, 99_999_999.99)]
    [InlineData(DiscountTypeEnum.FIXED, 0.01)]
    [InlineData(DiscountTypeEnum.PERCENT, 100)]
    [InlineData(DiscountTypeEnum.PERCENT, 0.5)]
    public async Task Snapshot_AcceptsValidValuesOfEachType(DiscountTypeEnum type, double value)
    {
        Assert.Null(await TrySaveSnapshotAsync(type, (decimal)value));
    }

    [Theory]
    [Trait("Catalog", "N-28")]
    [InlineData(DiscountTypeEnum.PERCENT, 100.01)]
    [InlineData(DiscountTypeEnum.PERCENT, 0)]
    [InlineData(DiscountTypeEnum.FIXED, 0)]
    [InlineData(DiscountTypeEnum.FIXED, 100_000_000)]
    public async Task Snapshot_RejectsOutOfRangeValuesOfEachType(DiscountTypeEnum type, double value)
    {
        var error = await TrySaveSnapshotAsync(type, (decimal)value);

        Assert.NotNull(error);
        Assert.Equal("chk_booking_promotions_discount_value", (error.InnerException as PostgresException)?.ConstraintName);
    }
}
