using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CancelPolicy;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class CancelPolicyService : ICancelPolicyService
    {
        private readonly ICancelPolicyRepository _policyRepo;

        public CancelPolicyService(ICancelPolicyRepository policyRepo)
        {
            _policyRepo = policyRepo;
        }

        // Lấy tất cả chính sách hủy
        public async Task<IEnumerable<CancelPolicyDto>> GetAllPolicesAsync()
        {
            var policies = await _policyRepo.GetAllAsync();
            return policies.Select(MapToDto);
        }

        // tạo mới chính sách hủy
        public async Task<CancelPolicyDto> CreatePolicyAsync(CreateCancelPolicyDto dto)
        {
            // Kiểm tra trùng lặp HoursBefore
            var existing = await _policyRepo.GetByHoursBeforeAsync(dto.HoursBefore);
            if (existing != null)
                throw new AppException(400, "Đã tồn tại chính sách cho mốc thời gian này");

            var policy = new CancelPolicy
            {
                HoursBefore = dto.HoursBefore,
                RefundPercent = dto.RefundPercent,
                Description = dto.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                var created = await _policyRepo.CreateAsync(policy);
                return MapToDto(created);
            }
            catch (DbUpdateException)
            {
                throw new AppException(400, "Đã tồn tại chính sách cho mốc thời gian này");
            }
        }

        // Cập nhật chính sách hủy
        public async Task<CancelPolicyDto> UpdatePolicyAsync(Guid id, UpdateCancelPolicyDto dto)
        {
            // Lấy chính sách cần cập nhật
            var policy = await _policyRepo.GetByIdAsync(id);
            if (policy == null)
                throw new AppException(404, "Không tìm thấy chính sách hủy");

            // Nếu sửa HoursBefore, cần check trùng lặp với policy khác
            if (policy.HoursBefore != dto.HoursBefore)
            {
                var existing = await _policyRepo.GetByHoursBeforeAsync(dto.HoursBefore);
                if (existing != null)
                    throw new AppException(400, "Đã tồn tại chính sách cho mốc thời gian này");
            }

            policy.HoursBefore = dto.HoursBefore;
            policy.RefundPercent = dto.RefundPercent;
            policy.Description = dto.Description;
            policy.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _policyRepo.UpdateAsync(policy);
            }
            catch (DbUpdateException)
            {
                throw new AppException(400, "Đã tồn tại chính sách cho mốc thời gian này");
            }

            return MapToDto(policy);
        }

        // Xóa chính sách hủy
        public async Task DeletePolicyAsync(Guid id)
        {
            // Lấy chính sách cần xóa
            var policy = await _policyRepo.GetByIdAsync(id);
            if (policy == null)
                throw new AppException(404, "Không tìm thấy chính sách hủy");

            // Phải giữ lại ít nhất 1 policy
            var count = await _policyRepo.CountAsync();
            if (count <= 1)
                throw new AppException(400, "Phải giữ lại ít nhất 1 chính sách hủy");

            await _policyRepo.DeleteAsync(policy);
        }

        // map CancelPolicy entity sang CancelPolicyDto
        private static CancelPolicyDto MapToDto(CancelPolicy policy)
        {
            return new CancelPolicyDto
            {
                Id = policy.Id,
                HoursBefore = policy.HoursBefore,
                RefundPercent = policy.RefundPercent,
                Description = policy.Description,
                CreatedAt = policy.CreatedAt,
                UpdatedAt = policy.UpdatedAt
            };
        }
    }
}
