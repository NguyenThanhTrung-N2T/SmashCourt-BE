using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Service;
using System;
using System.Threading.Tasks;

namespace SmashCourt_BE.Services.IService
{
    public interface IServiceService
    {
        // lấy danh sách dịch vụ với phân trang
        Task<PagedResult<ServiceDto>> GetAllAsync(PaginationQuery query);

        // tạo mới dịch vụ
        Task<ServiceDto> CreateAsync(CreateServiceDto dto);

        // cập nhật dịch vụ
        Task<ServiceDto> UpdateAsync(Guid id, UpdateServiceDto dto);

        // xóa dịch vụ
        Task DeleteAsync(Guid id);
    }
}

