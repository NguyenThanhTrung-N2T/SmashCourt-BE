using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// PaymentHoldPolicy (spec 7.5, P6-02; A7: 6A-2 chỉ làm dòng "đặt sân"). Hạn thanh toán và hạn giữ chỗ tuyệt đối tính từ đúng
/// mốc truyền vào, luôn Kind = Utc; đầu vào sai loại thời gian hoặc cấu hình vô lý bị từ chối trước khi tính.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
public class PaymentHoldPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc); // 10:00 giờ VN

    private static BookingPaymentSettings Settings(int initial = 10, int max = 15) =>
        new() { InitialHoldMinutes = initial, PaymentHoldMaxMinutes = max };

    [Fact]
    [Trait("Catalog", "BE-UT-08")]
    public void ForNewBooking_WithDefaultSettings_ReturnsTenAndFifteenMinutesAfterNow()
    {
        // Ví dụ của spec 7.5: đặt 10:00 ⇒ hạn thanh toán 10:10, hạn giữ chỗ 10:15
        var window = PaymentHoldPolicy.ForNewBooking(Now, new BookingPaymentSettings());

        Assert.Equal(Now.AddMinutes(10), window.ExpiresAt);
        Assert.Equal(Now.AddMinutes(15), window.HoldDeadline);
    }

    [Theory]
    [Trait("Catalog", "BE-UT-08")]
    [InlineData(1, 1)]
    [InlineData(1, 15)]
    [InlineData(10, 10)]
    [InlineData(20, 45)]
    [InlineData(60, 1440)]
    public void ForNewBooking_UsesConfiguredMinutes(int initial, int max)
    {
        var window = PaymentHoldPolicy.ForNewBooking(Now, Settings(initial, max));

        Assert.Equal(Now.AddMinutes(initial), window.ExpiresAt);
        Assert.Equal(Now.AddMinutes(max), window.HoldDeadline);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ForNewBooking_ResultIsAlwaysUtc()
    {
        var window = PaymentHoldPolicy.ForNewBooking(Now, Settings());

        Assert.Equal(DateTimeKind.Utc, window.ExpiresAt.Kind);
        Assert.Equal(DateTimeKind.Utc, window.HoldDeadline.Kind);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-08")]
    public void ForNewBooking_KeepsSubSecondPrecisionOfNow()
    {
        // Hạn bám đúng mốc tạo đơn, không làm tròn (Job-01 so sánh với now của nó)
        var now = Now.AddTicks(1234567);

        var window = PaymentHoldPolicy.ForNewBooking(now, Settings());

        Assert.Equal(now.AddMinutes(10), window.ExpiresAt);
        Assert.Equal(now.AddMinutes(15), window.HoldDeadline);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ForNewBooking_AcrossVietnamMidnight_AddsMinutesWithoutTimeZoneShift()
    {
        // 23:55 giờ VN = 16:55 UTC: hạn sang ngày VN mới nhưng vẫn là phép cộng phút trên UTC
        var now = new DateTime(2026, 11, 20, 16, 55, 0, DateTimeKind.Utc);

        var window = PaymentHoldPolicy.ForNewBooking(now, Settings());

        Assert.Equal(new DateTime(2026, 11, 20, 17, 5, 0, DateTimeKind.Utc), window.ExpiresAt);
        Assert.Equal(new DateTime(2026, 11, 20, 17, 10, 0, DateTimeKind.Utc), window.HoldDeadline);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-08")]
    public void ForNewBooking_ExpiresAtNeverAfterHoldDeadlineAndBothAfterNow()
    {
        foreach (var (initial, max) in new[] { (1, 1), (10, 15), (15, 15), (30, 600) })
        {
            var window = PaymentHoldPolicy.ForNewBooking(Now, Settings(initial, max));

            Assert.True(window.ExpiresAt > Now, $"initial={initial}, max={max}");
            Assert.True(window.ExpiresAt <= window.HoldDeadline, $"initial={initial}, max={max}");
        }
    }

    [Theory]
    [Trait("Catalog", "BE-UT-12")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ForNewBooking_WhenNowIsNotUtc_Throws(DateTimeKind kind)
    {
        var now = DateTime.SpecifyKind(Now, kind);

        var ex = Assert.Throws<ArgumentException>(() => PaymentHoldPolicy.ForNewBooking(now, Settings()));
        Assert.Equal("nowUtc", ex.ParamName);
    }

    [Fact]
    public void ForNewBooking_WhenSettingsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PaymentHoldPolicy.ForNewBooking(Now, null!));
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-1, 15)]
    public void ForNewBooking_WhenInitialHoldNotPositive_Throws(int initial, int max)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => PaymentHoldPolicy.ForNewBooking(Now, Settings(initial, max)));
        Assert.Contains(nameof(BookingPaymentSettings.InitialHoldMinutes), ex.Message);
    }

    [Theory]
    [InlineData(10, 9)]
    [InlineData(15, 0)]
    public void ForNewBooking_WhenHoldMaxBelowInitial_Throws(int initial, int max)
    {
        // Cấu hình đã được BookingPaymentSettingsValidator chặn lúc khởi động; hàm vẫn tự giữ bất biến expires ≤ deadline
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => PaymentHoldPolicy.ForNewBooking(Now, Settings(initial, max)));
        Assert.Contains(nameof(BookingPaymentSettings.PaymentHoldMaxMinutes), ex.Message);
    }
}
