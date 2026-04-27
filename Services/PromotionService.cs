using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Promotion;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class PromotionService : IPromotionService
    {
        private readonly IPromotionRepository _repo;
        private static readonly TimeZoneInfo VnTimezone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");


        public PromotionService(IPromotionRepository repo)
        {
            _repo = repo;
        }

        // Lấy tất cả khuyến mãi (có phân trang)
        public async Task<PagedResult<PromotionDto>> GetAllAsync(PaginationQuery query)
        {
            var pagedResult = await _repo.GetAllAsync(query.Page, query.PageSize);

            return new PagedResult<PromotionDto>
            {
                Items = pagedResult.Items.Select(MapToDto),
                TotalItems = pagedResult.TotalItems,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize
            };
        }

        // Lấy tất cả khuyến mãi đang ACTIVE (không phân trang)
        public async Task<List<PromotionDto>> GetActiveAsync()
        {
            var promotions = await _repo.GetActiveAsync();
            return promotions.Select(MapToDto).ToList();
        }

        // Lấy khuyến mãi theo id
        public async Task<PromotionDto> GetByIdAsync(Guid id)
        {
            var promotion = await _repo.GetByIdAsync(id);
            if (promotion == null)
                throw new AppException(404, "Không tìm thấy khuyến mãi", ErrorCodes.NotFound);

            return MapToDto(promotion);
        }

        // Tính status tự động theo ngày
        private static PromotionStatus CalculateStatus(DateOnly startDate, DateOnly endDate)
        {
            // Lấy ngày hiện tại theo timezone VN
            var vnNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, VnTimezone);
            var today = DateOnly.FromDateTime(vnNow);

            return (today >= startDate && today <= endDate)
                ? PromotionStatus.ACTIVE
                : PromotionStatus.INACTIVE;
        }

        // Tạo mới khuyến mãi
        public async Task<PromotionDto> CreateAsync(CreatePromotionDto dto)
        {
            // 1. Convert DateTime → DateOnly
            var startDate = DateOnly.FromDateTime(dto.StartDate);
            var endDate = DateOnly.FromDateTime(dto.EndDate);

            // 2. Validate start_date <= end_date
            if (startDate > endDate)
                throw new AppException(400,
                    "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc",
                    ErrorCodes.BadRequest);

            // 3. Tự tính status theo ngày
            var promotion = new Promotion
            {
                Name = dto.Name.Trim(),
                DiscountRate = dto.DiscountRate,
                StartDate = startDate,
                EndDate = endDate,
                Status = CalculateStatus(startDate, endDate),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await _repo.CreateAsync(promotion);
            return MapToDto(created);
        }

        // Cập nhật khuyến mãi
        public async Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionDto dto)
        {
            // 1. Tìm promotion
            var promotion = await _repo.GetByIdAsync(id);
            if (promotion == null)
                throw new AppException(404, "Không tìm thấy khuyến mãi", ErrorCodes.NotFound);

            // 2. Không cho sửa promotion đã DELETED
            if (promotion.Status == PromotionStatus.DELETED)
                throw new AppException(400,
                    "Không thể cập nhật khuyến mãi đã bị xóa", ErrorCodes.BadRequest);

            // 3. Convert DateTime → DateOnly
            var startDate = DateOnly.FromDateTime(dto.StartDate);
            var endDate = DateOnly.FromDateTime(dto.EndDate);

            // 4. Validate start_date <= end_date
            if (startDate > endDate)
                throw new AppException(400,
                    "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc",
                    ErrorCodes.BadRequest);

            // 5. Update + tính lại status
            promotion.Name = dto.Name.Trim();
            promotion.DiscountRate = dto.DiscountRate;
            promotion.StartDate = startDate;
            promotion.EndDate = endDate;
            promotion.Status = CalculateStatus(startDate, endDate);
            promotion.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(promotion);
            return MapToDto(promotion);
        }

        // Xóa khuyến mãi (soft delete)
        public async Task DeleteAsync(Guid id)
        {
            // 1. Tìm promotion
            var promotion = await _repo.GetByIdAsync(id);
            if (promotion == null)
                throw new AppException(404, "Không tìm thấy khuyến mãi", ErrorCodes.NotFound);

            // 2. Đã DELETED rồi → không cần làm gì
            if (promotion.Status == PromotionStatus.DELETED)
                throw new AppException(400,
                    "Khuyến mãi đã bị xóa trước đó", ErrorCodes.BadRequest);

            // 3. Xóa mềm
            promotion.Status = PromotionStatus.DELETED;
            promotion.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(promotion);
        }


        // map data entity → dto
        private static PromotionDto MapToDto(Promotion p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            DiscountRate = p.DiscountRate,
            StartDate = p.StartDate.ToDateTime(TimeOnly.MinValue),
            EndDate = p.EndDate.ToDateTime(TimeOnly.MinValue),
            Status = p.Status,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }
}
