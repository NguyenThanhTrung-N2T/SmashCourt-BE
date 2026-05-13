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
            var promotion = await _repo.GetByIdWithConditionsAsync(id);

            if (promotion == null)
                throw new AppException(404, "Không tìm thấy khuyến mãi", ErrorCodes.NotFound);

            return MapToDto(promotion);
        }

        // Tính status tự động theo ngày
        private static PromotionStatus CalculateStatus(DateOnly startDate, DateOnly endDate)
        {
            var today = SmashCourt_BE.Helpers.DateTimeHelper.GetTodayInVietnam();

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

            // 3. Validate code uniqueness if provided
            if (!string.IsNullOrWhiteSpace(dto.Code))
            {
                var codeExists = await _repo.CodeExistsAsync(dto.Code.Trim());
                if (codeExists)
                    throw new AppException(400, "Mã khuyến mãi đã tồn tại", ErrorCodes.BadRequest);
            }

            // 4. Validate discount type and value
            if (dto.DiscountType == DiscountTypeEnum.PERCENT && dto.DiscountValue > 100)
                throw new AppException(400, "Tỷ lệ giảm giá phần trăm không được vượt quá 100", ErrorCodes.BadRequest);

            // Validate discount value doesn't exceed database precision (10,2) = max 99999999.99
            if (dto.DiscountValue > 99999999.99m)
                throw new AppException(400, "Giá trị giảm giá vượt quá giới hạn cho phép", ErrorCodes.BadRequest);

            // Validate max discount amount if provided
            if (dto.MaxDiscountAmount.HasValue && dto.MaxDiscountAmount.Value > 99999999.99m)
                throw new AppException(400, "Giá trị giảm tối đa vượt quá giới hạn cho phép", ErrorCodes.BadRequest);

            // 5. Tự tính status theo ngày
            var promotion = new Promotion
            {
                Name = dto.Name.Trim(),
                Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim().ToUpper(),
                PromoDisplayUrl = dto.PromoDisplayUrl,
                Description = dto.Description,
                DiscountType = dto.DiscountType,
                DiscountValue = dto.DiscountValue,
                MaxDiscountAmount = dto.MaxDiscountAmount,
                UsageLimit = dto.UsageLimit,
                UsagePerUserLimit = dto.UsagePerUserLimit,
                UsedCount = 0,
                StartDate = startDate,
                EndDate = endDate,
                Status = CalculateStatus(startDate, endDate),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // 6. Add conditions if provided
            if (dto.Conditions != null && dto.Conditions.Any())
            {
                promotion.Conditions = dto.Conditions.Select(c => new PromotionCondition
                {
                    ConditionType = c.ConditionType.Trim(),
                    ConditionValue = c.ConditionValue.Trim()
                }).ToList();
            }

            var created = await _repo.CreateAsync(promotion);
            return MapToDto(created);
        }

        // Cập nhật khuyến mãi
        public async Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionDto dto)
        {
            // 1. Tìm promotion with conditions
            var promotion = await _repo.GetByIdWithConditionsAsync(id);

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

            // 5. Validate code uniqueness if changed
            if (!string.IsNullOrWhiteSpace(dto.Code))
            {
                var codeToCheck = dto.Code.Trim().ToUpper();
                var codeExists = await _repo.CodeExistsAsync(codeToCheck, id);
                if (codeExists)
                    throw new AppException(400, "Mã khuyến mãi đã tồn tại", ErrorCodes.BadRequest);
            }

            // 6. Validate discount type and value
            if (dto.DiscountType == DiscountTypeEnum.PERCENT && dto.DiscountValue > 100)
                throw new AppException(400, "Tỷ lệ giảm giá phần trăm không được vượt quá 100", ErrorCodes.BadRequest);

            // Validate discount value doesn't exceed database precision (12,2) = max 99999999.99
            if (dto.DiscountValue > 99999999.99m)
                throw new AppException(400, "Giá trị giảm giá vượt quá giới hạn cho phép", ErrorCodes.BadRequest);

            // Validate max discount amount if provided
            if (dto.MaxDiscountAmount.HasValue && dto.MaxDiscountAmount.Value > 99999999.99m)
                throw new AppException(400, "Giá trị giảm tối đa vượt quá giới hạn cho phép", ErrorCodes.BadRequest);

            // 7. Update + tính lại status
            promotion.Name = dto.Name.Trim();
            promotion.Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim().ToUpper();
            promotion.PromoDisplayUrl = dto.PromoDisplayUrl;
            promotion.Description = dto.Description;
            promotion.DiscountType = dto.DiscountType;
            promotion.DiscountValue = dto.DiscountValue;
            promotion.MaxDiscountAmount = dto.MaxDiscountAmount;
            promotion.UsageLimit = dto.UsageLimit;
            promotion.UsagePerUserLimit = dto.UsagePerUserLimit;
            promotion.StartDate = startDate;
            promotion.EndDate = endDate;
            promotion.Status = CalculateStatus(startDate, endDate);
            promotion.UpdatedAt = DateTime.UtcNow;

            // 8. Update conditions - remove old and add new
            await _repo.RemoveConditionsAsync(promotion.Id);
            if (dto.Conditions != null && dto.Conditions.Any())
            {
                promotion.Conditions = dto.Conditions.Select(c => new PromotionCondition
                {
                    PromotionId = promotion.Id,
                    ConditionType = c.ConditionType.Trim(),
                    ConditionValue = c.ConditionValue.Trim()
                }).ToList();
            }

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
            Code = p.Code,
            PromoDisplayUrl = p.PromoDisplayUrl,
            Description = p.Description,
            DiscountType = p.DiscountType,
            DiscountValue = p.DiscountValue,
            MaxDiscountAmount = p.MaxDiscountAmount,
            UsageLimit = p.UsageLimit,
            UsagePerUserLimit = p.UsagePerUserLimit,
            UsedCount = p.UsedCount,
            StartDate = p.StartDate.ToDateTime(TimeOnly.MinValue),
            EndDate = p.EndDate.ToDateTime(TimeOnly.MinValue),
            Status = p.Status,
            Conditions = p.Conditions?.Select(c => new PromotionConditionDto
            {
                ConditionType = c.ConditionType,
                ConditionValue = c.ConditionValue
            }).ToList()
        };
    }
}
