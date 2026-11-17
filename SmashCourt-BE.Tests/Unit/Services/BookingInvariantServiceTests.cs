using Moq;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>Service kiểm tính đúng: kiểm tham số trước khi đọc DB, đọc đúng phạm vi qua repository, trả số đơn đã soát cùng vi phạm.</summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "OB-BE-5")]
public sealed class BookingInvariantServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IBookingInvariantRepository> _repository = new();

    private BookingInvariantService Service() => new(_repository.Object);

    public static TheoryData<BookingScope> InvalidScopes() => new()
    {
        new BookingScope(null, null, null),
        new BookingScope(null, Now, null),
        new BookingScope(null, Now, Now.AddMinutes(-1)),
        new BookingScope(null, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), Now),
        new BookingScope(null, Now.AddHours(-1), Now, SettledBeforeUtc: DateTime.SpecifyKind(Now, DateTimeKind.Local)),
    };

    [Theory]
    [MemberData(nameof(InvalidScopes))]
    public async Task CheckAsync_WhenScopeIsInvalid_ThrowsWithoutReading(BookingScope scope)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CheckAsync(scope, Now));

        _repository.Verify(r => r.LoadAsync(It.IsAny<BookingScope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenNowIsNotUtc_ThrowsWithoutReading()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CheckAsync(BookingScope.Of(Guid.NewGuid()), DateTime.SpecifyKind(Now, DateTimeKind.Local)));

        _repository.Verify(r => r.LoadAsync(It.IsAny<BookingScope>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_WhenDataIsHealthy_ReturnsTheCheckedCountAndNoViolation()
    {
        var scope = new BookingScope(null, Now.AddHours(-24), Now, Now.AddMinutes(-2));
        var booking = new InvariantBookingRow(Guid.NewGuid(), BookingStatus.COMPLETED, null, null, null, null, Guid.NewGuid());
        _repository.Setup(r => r.LoadAsync(scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingInvariantSnapshot([booking.Id, Guid.NewGuid()], [booking], new Dictionary<Guid, InvariantInvoiceRow>(), [], [], []));

        var result = await Service().CheckAsync(scope, Now);

        Assert.Equal(2, result.CheckedBookings);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task CheckAsync_WhenDataIsBroken_ReturnsTheViolations()
    {
        var booking = new InvariantBookingRow(Guid.NewGuid(), BookingStatus.PENDING, null, null, null, null, Guid.NewGuid());
        _repository.Setup(r => r.LoadAsync(It.IsAny<BookingScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingInvariantSnapshot([booking.Id], [booking], new Dictionary<Guid, InvariantInvoiceRow>(), [], [], []));

        var result = await Service().CheckAsync(BookingScope.Of(booking.Id), Now);

        var violation = Assert.Single(result.Violations);
        Assert.Equal(InvariantRules.PendingHasHoldDeadline, violation.Rule);
        Assert.Equal(booking.Id, violation.BookingId);
    }
}
