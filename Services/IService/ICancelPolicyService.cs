using SmashCourt_BE.DTOs.CancelPolicy;

namespace SmashCourt_BE.Services.IService
{
    public interface ICancelPolicyService
    {
        // lấy tất cả chính sách hủy
        Task<IEnumerable<CancelPolicyDto>> GetAllPolicesAsync();

        // tạo mới một chính sách hủy
        Task<CancelPolicyDto> CreatePolicyAsync(CreateCancelPolicyDto dto);

        // cập nhật một chính sách hủy
        Task<CancelPolicyDto> UpdatePolicyAsync(Guid id, UpdateCancelPolicyDto dto);

        // xóa một chính sách hủy
        Task DeletePolicyAsync(Guid id);
    }
}
