using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Promotion;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Promotions;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Services
{
    public class PromotionService : IPromotionService
    {
        private readonly IPromotionRepository _repo;
        private readonly PromotionEngineService _promotionEngine;
        private readonly ICourtRepository _courtRepo;
        private readonly IBookingRepository _bookingRepo;

        public PromotionService(
            IPromotionRepository repo,
            PromotionEngineService promotionEngine,
            ICourtRepository courtRepo,
            IBookingRepository bookingRepo)
        {
            _repo = repo;
            _promotionEngine = promotionEngine;
            _courtRepo = courtRepo;
            _bookingRepo = bookingRepo;
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

        // Lấy danh sách promotion áp dụng được cho booking context cụ thể
        public async Task<List<ApplicablePromotionDto>> GetApplicablePromotionsAsync(
            GetApplicablePromotionsDto dto, Guid customerId)
        {
            // 1. Lấy tất cả promotion ACTIVE
            var activePromotions = await _repo.GetActiveWithConditionsAsync();

            // 2. Lấy thông tin court để có CourtType (Sport)
            var court = await _courtRepo.GetByIdAsync(dto.CourtId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // 3. Lấy số lượng booking đã hoàn thành của customer
            var previousBookingCount = await _bookingRepo.GetCompletedBookingCountAsync(customerId);

            // 4. Build promotion context
            var context = new PromotionContext
            {
                UserId = customerId,
                BranchId = dto.BranchId,
                CourtId = dto.CourtId,
                BookingAmount = dto.BookingAmount,
                BookingDate = dto.BookingDate,
                Sport = court.CourtType?.Name ?? "Unknown",
                PreviousBookingCount = previousBookingCount
            };

            // 5. Validate từng promotion và tính discount
            var applicablePromotions = new List<ApplicablePromotionDto>();

            foreach (var promotion in activePromotions)
            {
                // Validate promotion trực tiếp với promotion object (không cần lookup lại)
                var validationResult = await ValidatePromotionDirectAsync(promotion, context);

                if (validationResult.IsValid)
                {
                    // Lấy user usage count
                    var userUsageCount = 0;
                    if (promotion.UsagePerUserLimit.HasValue)
                    {
                        userUsageCount = await _repo.GetUserUsageCountAsync(promotion.Id, customerId);
                    }

                    applicablePromotions.Add(new ApplicablePromotionDto
                    {
                        Id = promotion.Id,
                        Name = promotion.Name,
                        Code = promotion.Code,
                        PromoDisplayUrl = promotion.PromoDisplayUrl,
                        Description = promotion.Description,
                        DiscountType = promotion.DiscountType,
                        DiscountValue = promotion.DiscountValue,
                        MaxDiscountAmount = promotion.MaxDiscountAmount,
                        StartDate = promotion.StartDate.ToDateTime(TimeOnly.MinValue),
                        EndDate = promotion.EndDate.ToDateTime(TimeOnly.MinValue),
                        DiscountAmount = validationResult.DiscountAmount,
                        FinalAmount = validationResult.FinalAmount,
                        UsageLimit = promotion.UsageLimit,
                        UsedCount = promotion.UsedCount,
                        RemainingUsage = promotion.UsageLimit.HasValue
                            ? Math.Max(0, promotion.UsageLimit.Value - promotion.UsedCount)
                            : null,
                        UsagePerUserLimit = promotion.UsagePerUserLimit,
                        UserUsageCount = userUsageCount,
                        RemainingUserUsage = promotion.UsagePerUserLimit.HasValue
                            ? Math.Max(0, promotion.UsagePerUserLimit.Value - userUsageCount)
                            : null
                    });
                }
            }

            // 6. Sắp xếp theo discount amount giảm dần (promotion tốt nhất lên đầu)
            return applicablePromotions
                .OrderByDescending(p => p.DiscountAmount)
                .ToList();
        }

        // Validate promotion trực tiếp từ object (không cần lookup lại bằng code)
        private async Task<PromotionValidationResult> ValidatePromotionDirectAsync(
            Models.Entities.Promotion promotion, PromotionContext context)
        {
            // 1. Kiểm tra ngày hiệu lực
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today < promotion.StartDate || today > promotion.EndDate)
                return new PromotionValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Mã khuyến mãi chưa có hiệu lực hoặc đã hết hạn"
                };

            // 2. Kiểm tra tổng lượt sử dụng
            if (promotion.UsageLimit.HasValue && promotion.UsedCount >= promotion.UsageLimit.Value)
                return new PromotionValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Mã khuyến mãi đã hết lượt sử dụng"
                };

            // 3. Kiểm tra lượt sử dụng theo user
            if (promotion.UsagePerUserLimit.HasValue)
            {
                var userUsageCount = await _repo.GetUserUsageCountAsync(promotion.Id, context.UserId);
                if (userUsageCount >= promotion.UsagePerUserLimit.Value)
                    return new PromotionValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Bạn đã sử dụng hết lượt áp dụng mã khuyến mãi này"
                    };
            }

            // 4. Đánh giá các conditions dùng PromotionEngineService logic
            var conditionResult = EvaluatePromotionConditions(promotion, context);
            if (!conditionResult.IsValid)
                return conditionResult;

            // 5. Tính discount
            var discountAmount = PromotionHelper.CalculateDiscount(promotion, context.BookingAmount);
            var finalAmount = Math.Max(0, context.BookingAmount - discountAmount);

            return new PromotionValidationResult
            {
                IsValid = true,
                Promotion = promotion,
                DiscountAmount = discountAmount,
                FinalAmount = finalAmount
            };
        }

        // Evaluate promotion conditions - copy logic từ PromotionEngineService
        private static PromotionValidationResult EvaluatePromotionConditions(
            Models.Entities.Promotion promotion, PromotionContext context)
        {
            if (promotion.Conditions == null || !promotion.Conditions.Any())
                return new PromotionValidationResult { IsValid = true };

            foreach (var condition in promotion.Conditions)
            {
                switch (condition.ConditionType)
                {
                    case "MIN_BOOKING_AMOUNT":
                        if (!decimal.TryParse(condition.ConditionValue, out var minAmount)
                            || context.BookingAmount < minAmount)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Giá trị đơn hàng tối thiểu phải từ {minAmount:N0} VNĐ"
                            };
                        break;

                    case "MAX_PREVIOUS_BOOKINGS":
                        if (!int.TryParse(condition.ConditionValue, out var maxBookings)
                            || context.PreviousBookingCount > maxBookings)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Khuyến mãi này chỉ dành cho khách hàng mới"
                            };
                        break;

                    case "BRANCH_ID":
                        if (context.BranchId.ToString() != condition.ConditionValue)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Mã khuyến mãi không áp dụng cho chi nhánh này"
                            };
                        break;

                    case "COURT_ID":
                        if (context.CourtId.ToString() != condition.ConditionValue)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Mã khuyến mãi không áp dụng cho sân này"
                            };
                        break;

                    case "SPORT":
                        if (context.Sport != condition.ConditionValue)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng cho môn {condition.ConditionValue}"
                            };
                        break;

                    case "DAY_OF_WEEK":
                        var dayOfWeek = context.BookingDate.DayOfWeek.ToString().ToUpper();
                        if (dayOfWeek != condition.ConditionValue.ToUpper())
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng vào {condition.ConditionValue}"
                            };
                        break;

                    case "START_HOUR":
                        if (!int.TryParse(condition.ConditionValue, out var startHour)
                            || context.BookingDate.Hour < startHour)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng từ {startHour}h trở đi"
                            };
                        break;

                    case "END_HOUR":
                        if (!int.TryParse(condition.ConditionValue, out var endHour)
                            || context.BookingDate.Hour >= endHour)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng trước {endHour}h"
                            };
                        break;

                    case "MONTH":
                        if (!int.TryParse(condition.ConditionValue, out var month)
                            || context.BookingDate.Month != month)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng trong tháng {month}"
                            };
                        break;

                    case "DAYS_OF_MONTH":
                        if (!int.TryParse(condition.ConditionValue, out var dayOfMonth)
                            || context.BookingDate.Day != dayOfMonth)
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng vào ngày {dayOfMonth} trong tháng"
                            };
                        break;

                    default:
                        // Unknown condition — skip (forward-compatible)
                        break;
                }
            }

            return new PromotionValidationResult { IsValid = true };
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
