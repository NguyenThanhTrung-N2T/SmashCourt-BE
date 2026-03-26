using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using System.Threading.Tasks;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IServiceRepository
    {
        Task<PagedResult<Service>> GetAllAsync(int page = 1, int pageSize = 10);
        Task<Service?> GetByIdAsync(Guid id);
        Task<Service?> GetByNameAsync(string name);
        Task<Service> CreateAsync(Service service);
        Task<Service?> UpdateAsync(Service service);
        Task<bool> SoftDeleteAsync(Guid id);
        Task<int> GetActiveBranchCountAsync(Guid id);
    }
}

