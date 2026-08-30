using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class BranchUserServiceTests
{
    [Fact]
    public async Task SearchUsersAsync_DelegatesQueryAndPreservesPaging()
    {
        var query = new SmashCourt_BE.DTOs.BranchManagement.UserSearchQuery();
        var repository = new Mock<IUserRepository>();
        repository.Setup(x => x.SearchUsersAsync(query)).ReturnsAsync(new PagedResult<SmashCourt_BE.Models.Entities.User>
        {
            Items = [], Page = 2, PageSize = 10, TotalItems = 0
        });

        var result = await new BranchUserService(repository.Object, Mock.Of<IUserBranchRepository>()).SearchUsersAsync(query);

        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        repository.Verify(x => x.SearchUsersAsync(query), Times.Once);
    }
}
