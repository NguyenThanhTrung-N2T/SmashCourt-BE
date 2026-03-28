using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Service;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class ServiceService : IServiceService
    {
        private readonly IServiceRepository _repo;

        public ServiceService(IServiceRepository repo)
        {
            _repo = repo;
        }

        // lấy danh sách dịch vụ đang hoạt động với phân trang
        public async Task<PagedResult<ServiceDto>> GetAllAsync(PaginationQuery query)
        {
            var pagedResult = await _repo.GetAllAsync(query.Page, query.PageSize);

            return new PagedResult<ServiceDto>
            {
                Items = pagedResult.Items.Select(MapToDto),
                TotalItems = pagedResult.TotalItems,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize
            };
        }

        // tạo mới dịch vụ
        public async Task<ServiceDto> CreateAsync(CreateServiceDto dto)
        {
            // 1. Check tên unique
            var exists = await _repo.ExistsByNameAsync(dto.Name);
            if (exists)
                throw new AppException(409, "Tên dịch vụ đã tồn tại", ErrorCodes.NameDuplicate);

            var service = new Service
            {
                Name = dto.Name.Trim(),
                Description = dto.Description?.Trim(),
                Unit = dto.Unit.Trim(),
                DefaultPrice = dto.DefaultPrice,
                Status = ServiceStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                var created = await _repo.CreateAsync(service);
                return MapToDto(created);
            }
            catch (DbUpdateException)
            {
                throw new AppException(400, "Tên dịch vụ đã tồn tại", ErrorCodes.NameDuplicate);
            }
        }


        // cập nhật dịch vụ
        public async Task<ServiceDto> UpdateAsync(Guid id, UpdateServiceDto dto)
        {
            // 1. Tìm service
            var service = await _repo.GetByIdAsync(id);
            if (service == null)
                throw new AppException(404, "Không tìm thấy dịch vụ", ErrorCodes.NotFound);

            // 2. Check tên unique — bỏ qua chính nó
            var exists = await _repo.ExistsByNameAsync(dto.Name, id);
            if (exists)
                throw new AppException(409, "Tên dịch vụ đã tồn tại", ErrorCodes.NameDuplicate);

            // 3. Update
            service.Name = dto.Name.Trim();
            service.Description = dto.Description?.Trim();
            service.Unit = dto.Unit.Trim();
            service.DefaultPrice = dto.DefaultPrice;
            service.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _repo.UpdateAsync(service);
            }
            catch (DbUpdateException)
            {
                throw new AppException(409, "Tên dịch vụ đã tồn tại", ErrorCodes.NameDuplicate);
            }

            return MapToDto(service);
        }

        // xóa mềm dịch vụ
        public async Task DeleteAsync(Guid id)
        {
            var service = await _repo.GetByIdAsync(id);
            if (service == null)
                throw new AppException(404, "Không tìm thấy dịch vụ", ErrorCodes.NotFound);

            service.Status = ServiceStatus.DELETED;
            service.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(service);
        }

        // map dữ liệu từ entity sang dto
        protected static ServiceDto MapToDto(Service s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            Unit = s.Unit,
            DefaultPrice = s.DefaultPrice,
            Status = s.Status,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }
}
