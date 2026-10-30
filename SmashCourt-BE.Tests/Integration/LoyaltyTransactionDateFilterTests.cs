using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// T-11 trên schema thật (BE-IT-25, phần lọc giao dịch điểm; plan 6A-6 Task 8): <c>fromDate</c>/<c>toDate</c> của query là ngày theo giờ VN,
/// chỉ dùng phần ngày; <c>toDate</c> tính hết ngày. Mã cũ so thẳng <see cref="DateTime"/> của query (<c>Kind = Unspecified</c>) với cột
/// timestamptz nên Npgsql ném lỗi (500); kể cả khi không ném thì <c>toDate</c> chỉ tới 00:00 UTC và lệch ngày VN.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-25")]
public sealed class LoyaltyTransactionDateFilterTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime Nov20 = new(2020, 11, 20); // như model binding đọc "2020-11-20": Kind = Unspecified

    private CustomerManagementRepository Repository => new(DbContext);

    // Giao dịch theo giờ VN, tên là nhãn để so kết quả
    private static readonly (string Label, DateTime CreatedAtUtc)[] Moments =
    [
        ("19/11 23:50", Utc(2020, 11, 19, 16, 50, 0)),
        ("20/11 00:00", Utc(2020, 11, 19, 17, 0, 0)),
        ("20/11 23:30", Utc(2020, 11, 20, 16, 30, 0)),
        ("20/11 23:59:59.999999", Utc(2020, 11, 20, 16, 59, 59).AddTicks(9_999_990)),
        ("21/11 00:00", Utc(2020, 11, 20, 17, 0, 0)),
        ("21/11 00:10", Utc(2020, 11, 20, 17, 10, 0)),
    ];

    private static DateTime Utc(int y, int m, int d, int h, int min, int s) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    private async Task<User> SeedTransactionsAsync()
    {
        var customer = await Seeder.SeedCustomerAsync();
        foreach (var (label, createdAt) in Moments)
        {
            DbContext.LoyaltyTransactions.Add(new LoyaltyTransaction
            {
                Id = Guid.NewGuid(),
                UserId = customer.Id,
                Points = 10,
                TotalPointsAfter = 10,
                Type = LoyaltyTransactionType.ADJUST,
                Note = label,
                CreatedAt = createdAt
            });
        }
        await DbContext.SaveChangesAsync();
        return customer;
    }

    private async Task<string[]> LabelsAsync(Guid customerId, DateTime? from, DateTime? to)
    {
        var result = await Repository.GetLoyaltyTransactionsAsync(
            customerId, new LoyaltyTransactionQuery { FromDate = from, ToDate = to, PageSize = 50 });
        Assert.Equal(result.Items.Count(), result.TotalItems);
        return result.Items.Select(t => t.Note!).OrderBy(n => Array.FindIndex(Moments, m => m.Label == n)).ToArray();
    }

    [Fact]
    public async Task SingleDay_ReturnsTheWholeVietnamDayOnly()
    {
        var customer = await SeedTransactionsAsync();

        Assert.Equal(
            ["20/11 00:00", "20/11 23:30", "20/11 23:59:59.999999"],
            await LabelsAsync(customer.Id, Nov20, Nov20));
    }

    [Fact]
    public async Task FromDateOnly_StartsAtMidnightVietnam()
    {
        var customer = await SeedTransactionsAsync();

        Assert.Equal(
            ["20/11 00:00", "20/11 23:30", "20/11 23:59:59.999999", "21/11 00:00", "21/11 00:10"],
            await LabelsAsync(customer.Id, Nov20, null));
    }

    [Fact]
    public async Task ToDateOnly_IncludesTheWholeLastDay()
    {
        var customer = await SeedTransactionsAsync();

        Assert.Equal(
            ["19/11 23:50", "20/11 00:00", "20/11 23:30", "20/11 23:59:59.999999"],
            await LabelsAsync(customer.Id, null, Nov20));
    }

    [Fact]
    public async Task NoFilter_ReturnsEverything()
    {
        var customer = await SeedTransactionsAsync();

        Assert.Equal(Moments.Select(m => m.Label).ToArray(), await LabelsAsync(customer.Id, null, null));
    }

    // Chỉ phần ngày có nghĩa: giờ hay Kind của giá trị gửi lên không đổi kết quả
    [Theory]
    [InlineData(DateTimeKind.Unspecified, 0)]
    [InlineData(DateTimeKind.Unspecified, 15)]
    [InlineData(DateTimeKind.Utc, 0)]
    [InlineData(DateTimeKind.Utc, 23)]
    [InlineData(DateTimeKind.Local, 12)]
    public async Task OnlyTheDatePartMatters(DateTimeKind kind, int hour)
    {
        var customer = await SeedTransactionsAsync();
        var value = DateTime.SpecifyKind(Nov20.AddHours(hour), kind);

        Assert.Equal(
            ["20/11 00:00", "20/11 23:30", "20/11 23:59:59.999999"],
            await LabelsAsync(customer.Id, value, value));
    }

    [Fact]
    public async Task ReversedRange_ReturnsNothing()
    {
        var customer = await SeedTransactionsAsync();

        Assert.Empty(await LabelsAsync(customer.Id, Nov20.AddDays(1), Nov20));
    }

    [Fact]
    public async Task OtherCustomersTransactions_AreNotIncluded()
    {
        var customer = await SeedTransactionsAsync();
        await SeedTransactionsAsync();

        Assert.Equal(3, (await LabelsAsync(customer.Id, Nov20, Nov20)).Length);
    }
}
