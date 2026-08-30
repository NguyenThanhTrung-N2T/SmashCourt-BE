using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Services;

public class ReportServiceTests
{
    [Fact]
    public async Task GetRevenueReportAsync_ReversedDateRangeThrowsBadRequest()
    {
        var service = new ReportService(Mock.Of<IReportRepository>(), Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchScopeResolver>(), new MemoryCache(new MemoryCacheOptions()), Mock.Of<ILogger<ReportService>>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetRevenueReportAsync(new ReportFilterDto
        {
            FromDate = new DateOnly(2026, 6, 20),
            ToDate = new DateOnly(2026, 6, 1)
        }, Guid.NewGuid(), "OWNER"));

        Assert.Equal(ErrorCodes.BadRequest, exception.ErrorCode);
    }
}
