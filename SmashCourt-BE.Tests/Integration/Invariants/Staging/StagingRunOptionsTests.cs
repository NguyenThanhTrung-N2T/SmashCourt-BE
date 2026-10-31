namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// Đọc cấu hình lượt kiểm staging từ biến môi trường (plan 6A-6 Task 11). Chỉ lớp này đổi các biến <c>SC6_*</c>, <c>TADSS_*</c> (các test
/// trong một lớp chạy tuần tự) và trả lại giá trị cũ sau mỗi test.
/// </summary>
[Trait("Catalog", "BE-IT-20")]
public sealed class StagingRunOptionsTests : IDisposable
{
    private static readonly string[] Variables =
        [StagingRunOptions.ConnectionStringVariable, "SC6_FROM_UTC", "SC6_TO_UTC", "TADSS_BASE_URL", "TADSS_API_KEY", "SC6_OUTPUT"];

    private readonly Dictionary<string, string?> _saved = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
            Environment.SetEnvironmentVariable(name, value);
    }

    private static void Set(string? connection = "Host=x;Database=y", string? from = "2026-12-01T00:00:00Z", string? to = "2026-12-02T00:00:00Z",
        string? tadss = null, string? key = null, string? output = null)
    {
        Environment.SetEnvironmentVariable(StagingRunOptions.ConnectionStringVariable, connection);
        Environment.SetEnvironmentVariable("SC6_FROM_UTC", from);
        Environment.SetEnvironmentVariable("SC6_TO_UTC", to);
        Environment.SetEnvironmentVariable("TADSS_BASE_URL", tadss);
        Environment.SetEnvironmentVariable("TADSS_API_KEY", key);
        Environment.SetEnvironmentVariable("SC6_OUTPUT", output);
    }

    [Fact]
    public void ReadsEveryVariable_AndNormalizesToUtc()
    {
        Set(from: "2026-12-01T09:00:00+07:00", to: "2026-12-02T00:00:00Z", tadss: "https://tadss.example", key: "k", output: "out/sc6.json");

        var options = StagingRunOptions.FromEnvironment();

        Assert.Equal(new DateTime(2026, 12, 1, 2, 0, 0, DateTimeKind.Utc), options.FromUtc);
        Assert.Equal(DateTimeKind.Utc, options.FromUtc.Kind);
        Assert.Equal(new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc), options.ToUtc);
        Assert.Equal(new Uri("https://tadss.example/"), options.TadssBaseUrl); // thêm "/" để ghép đường dẫn tương đối
        Assert.Equal("k", options.TadssApiKey);
        Assert.Equal("out/sc6.json", options.OutputPath);
    }

    [Theory]
    [InlineData(null, "2026-12-01T00:00:00Z", "2026-12-02T00:00:00Z", "SC6_CONNECTION_STRING")]
    [InlineData("Host=x", null, "2026-12-02T00:00:00Z", "SC6_FROM_UTC")]
    [InlineData("Host=x", "2026-12-01T00:00:00Z", "", "SC6_TO_UTC")]
    [InlineData("Host=x", "hôm qua", "2026-12-02T00:00:00Z", "SC6_FROM_UTC")]
    public void MissingOrInvalidVariable_StopsWithItsName(string? connection, string? from, string? to, string variable)
    {
        Set(connection, from, to);

        var ex = Assert.Throws<InvalidOperationException>(StagingRunOptions.FromEnvironment);
        Assert.Contains(variable, ex.Message);
    }

    [Fact]
    public void StagingFact_IsSkippedWithoutConnectionString_AndRunsWithIt()
    {
        Environment.SetEnvironmentVariable(StagingRunOptions.ConnectionStringVariable, null);
        Assert.Contains("SC6_CONNECTION_STRING", new StagingFactAttribute().Skip);

        Environment.SetEnvironmentVariable(StagingRunOptions.ConnectionStringVariable, "Host=x");
        Assert.Null(new StagingFactAttribute().Skip);
    }
}
