using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Service;
using System;
using System.Threading.Tasks;

namespace SmashCourt_BE.Services.IService
{
    public interface IServiceService
    {
        Task<PagedResult<ServiceDto>> GetAllServicesAsync(int page = 1, int pageSize = 10);
        Task<ServiceDto> GetServiceByIdAsync(Guid id);
        Task<ServiceDto> CreateServiceAsync(CreateServiceDto dto);
        Task<ServiceDto> UpdateServiceAsync(Guid id, UpdateServiceDto dto);
        Task DeleteServiceAsync(Guid id);

        // Branch override
        Task<PagedResult<BranchServiceDto>> GetBranchServicesAsync(Guid branchId, int page = 1, int pageSize = 10);
        Task<BranchServiceDto> CreateBranchServiceAsync(Guid branchId, CreateBranchServiceDto dto);
        Task<BranchServiceDto> UpdateBranchServiceAsync(Guid branchId, Guid serviceId, CreateBranchServiceDto dto);
        Task DeleteBranchServiceAsync(Guid branchId, Guid serviceId);
    }
}

