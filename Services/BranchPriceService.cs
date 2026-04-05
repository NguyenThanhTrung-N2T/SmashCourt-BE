using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BranchPriceService : IBranchPriceService
    {
        private readonly IBranchPriceRepository _repo;
        private readonly ISystemPriceRepository _systemPriceRepo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly IBranchRepository _branchRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly ICustomerLoyaltyRepository _loyaltyRepo;

        public BranchPriceService(
            IBranchPriceRepository repo,
            ISystemPriceRepository systemPriceRepo,
            ITimeSlotRepository timeSlotRepo,
            IBranchRepository branchRepo,
            ICourtRepository courtRepo,
            ICustomerLoyaltyRepository loyaltyRepo)
        {
            _repo = repo;
            _systemPriceRepo = systemPriceRepo;
            _timeSlotRepo = timeSlotRepo;
            _branchRepo = branchRepo;
            _courtRepo = courtRepo;
            _loyaltyRepo = loyaltyRepo;
        }

        public async Task<List<CurrentPriceDto>> GetAllAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            await ValidateBranchAsync(branchId);
            var prices = await _repo.GetAllAsync(branchId, courtTypeId);
            return GroupPrices(prices);
        }

        public async Task<List<CurrentPriceDto>> GetCurrentAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            await ValidateBranchAsync(branchId);
            var prices = await _repo.GetCurrentAsync(branchId, courtTypeId);
            return GroupPrices(prices);
        }

        public async Task CreateBatchAsync(Guid branchId, CreateBranchPriceDto dto)
        {
            // 1. Validate branch
            await ValidateBranchAsync(branchId);

            // 2. Validate effective_from
            var today = DateTimeHelper.GetTodayInVietnam();
            if (dto.EffectiveFrom < today)
                throw new AppException(400,
                    "Ngày hiệu lực không thể là ngày trong quá khứ",
                    ErrorCodes.BadRequest);

            // 3. Validate court type belongs to branch
            var isCourtTypeEnabled = await _branchRepo.IsCourtTypeEnabledAsync(branchId, dto.CourtTypeId);
            if (!isCourtTypeEnabled)
                throw new AppException(400,
                    "Loại sân không hợp lệ hoặc không thuộc chi nhánh này",
                    ErrorCodes.BadRequest);

            // 3. Build danh sách prices
            var overrides = new List<BranchPriceOverride>();

            foreach (var slotPrice in dto.Prices)
            {
                var slots = await _timeSlotRepo.GetByTimeRangeAsync(
                    slotPrice.StartTime, slotPrice.EndTime);

                if (slots.Count != 2)
                    throw new AppException(400,
                        $"Khung giờ {slotPrice.StartTime:HH\\:mm} - {slotPrice.EndTime:HH\\:mm} không hợp lệ",
                        ErrorCodes.BadRequest);

                var weekdaySlot = slots.First(ts => ts.DayType == DayType.WEEKDAY);
                var weekendSlot = slots.First(ts => ts.DayType == DayType.WEEKEND);

                // Check trùng
                if (await _repo.ExistsAsync(branchId, dto.CourtTypeId, weekdaySlot.Id, dto.EffectiveFrom) ||
                    await _repo.ExistsAsync(branchId, dto.CourtTypeId, weekendSlot.Id, dto.EffectiveFrom))
                    throw new AppException(409,
                        $"Đã tồn tại giá override cho khung giờ {slotPrice.StartTime:HH\\:mm} - {slotPrice.EndTime:HH\\:mm}",
                        ErrorCodes.Conflict);

                overrides.Add(new BranchPriceOverride
                {
                    BranchId = branchId,
                    CourtTypeId = dto.CourtTypeId,
                    TimeSlotId = weekdaySlot.Id,
                    Price = slotPrice.WeekdayPrice,
                    EffectiveFrom = dto.EffectiveFrom,
                    CreatedAt = DateTime.UtcNow
                });

                overrides.Add(new BranchPriceOverride
                {
                    BranchId = branchId,
                    CourtTypeId = dto.CourtTypeId,
                    TimeSlotId = weekendSlot.Id,
                    Price = slotPrice.WeekendPrice,
                    EffectiveFrom = dto.EffectiveFrom,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _repo.CreateBatchAsync(overrides);
        }

        public async Task DeleteAsync(Guid branchId, Guid id)
        {
            await ValidateBranchAsync(branchId);

            var override_ = await _repo.GetByIdAsync(id);
            if (override_ == null || override_.BranchId != branchId)
                throw new AppException(404,
                    "Không tìm thấy cấu hình giá", ErrorCodes.NotFound);

            var today = DateTimeHelper.GetTodayInVietnam();
            if (override_.EffectiveFrom <= today)
                throw new AppException(400,
                    "Không thể xóa cấu hình giá đã hoặc đang có hiệu lực", ErrorCodes.BadRequest);

            await _repo.DeleteAsync(id);
        }

        public async Task<CalculatePriceResultDto> CalculateAsync(
            Guid branchId, CalculatePriceDto dto)
        {
            // 1. Validate
            if (dto.StartTime >= dto.EndTime)
                throw new AppException(400,
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc", ErrorCodes.BadRequest);
            // Booking date không được là ngày trong quá khứ            
            var today = DateTimeHelper.GetTodayInVietnam();
            if (dto.BookingDate < today)
                throw new AppException(400,
                    "Không thể tính giá cho ngày trong quá khứ", ErrorCodes.BadRequest);

            // 2. Validate branch + tìm court → lấy courtTypeId
            await ValidateBranchAsync(branchId);

            var court = await _courtRepo.GetByIdAsync(dto.CourtId, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);
            if (court.Status == CourtStatus.SUSPENDED || court.Status == CourtStatus.LOCKED)
                throw new AppException(400, "Sân hiện đang bị khóa hoặc bảo trì", ErrorCodes.BadRequest);

            // 3. Xác định WEEKDAY / WEEKEND
            var dayOfWeek = dto.BookingDate.DayOfWeek;
            var dayType = (dayOfWeek == DayOfWeek.Saturday ||
                           dayOfWeek == DayOfWeek.Sunday)
                ? DayType.WEEKEND
                : DayType.WEEKDAY;

            // 4. Lấy time slots theo dayType tại DB — sort theo startTime
            var relevantSlots = await _timeSlotRepo.GetByDayTypeAsync(dayType);

            if (!relevantSlots.Any())
                throw new AppException(400,
                    "Chưa cấu hình khung giờ cho hệ thống", ErrorCodes.BadRequest);

            // 5. Lấy giá hiện tại — branch override + system price
            var branchPrices = await _repo.GetCurrentAsync(branchId, court.CourtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentAsync(court.CourtTypeId);

            // 6. Chia sub-slot + tính tiền
            var breakdown = new List<PriceBreakdownDto>();
            decimal courtFee = 0;

            foreach (var slot in relevantSlots)
            {
                // Tìm overlap giữa slot và range đặt sân
                var overlapStart = slot.StartTime > dto.StartTime
                    ? slot.StartTime
                    : dto.StartTime;
                var overlapEnd = slot.EndTime < dto.EndTime
                    ? slot.EndTime
                    : dto.EndTime;

                if (overlapStart >= overlapEnd) continue;

                // Tính số giờ overlap
                var hours = (decimal)(overlapEnd - overlapStart).TotalHours;

                // Tìm giá — ưu tiên branch override
                var branchPrice = branchPrices.FirstOrDefault(p =>
                    p.TimeSlot.StartTime == slot.StartTime &&
                    p.TimeSlot.EndTime == slot.EndTime);

                var systemPrice = systemPrices.FirstOrDefault(p =>
                    p.TimeSlot.StartTime == slot.StartTime &&
                    p.TimeSlot.EndTime == slot.EndTime);

                decimal unitPrice = branchPrice?.Price > 0
                    ? branchPrice.Price
                    : systemPrice?.Price ?? 0;

                string priceSource = branchPrice?.Price > 0
                    ? "BRANCH_OVERRIDE"
                    : "SYSTEM_PRICE";

                if (unitPrice == 0)
                    throw new AppException(400,
                        $"Chưa cấu hình giá cho khung giờ {slot.StartTime:HH\\:mm} - {slot.EndTime:HH\\:mm}",
                        ErrorCodes.BadRequest);

                var subTotal = unitPrice * hours;
                courtFee += subTotal;

                breakdown.Add(new PriceBreakdownDto
                {
                    StartTime = overlapStart,
                    EndTime = overlapEnd,
                    UnitPrice = unitPrice,
                    Hours = hours,
                    SubTotal = subTotal,
                    PriceSource = priceSource
                });
            }

            // Nếu không có breakdown nào → nghĩa là thời gian đặt nằm ngoài giờ hoạt động của sân
            if (breakdown.Count == 0)
                throw new AppException(400,
                    "Thời gian đặt nằm ngoài giờ hoạt động của sân", ErrorCodes.BadRequest);

            return new CalculatePriceResultDto
            {
                CourtFee = courtFee,
                Breakdown = breakdown
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private async Task ValidateBranchAsync(Guid branchId)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
        }

        private static List<CurrentPriceDto> GroupPrices(List<BranchPriceOverride> prices)
        {
            return prices
                .GroupBy(bp => new
                {
                    bp.CourtTypeId,
                    bp.TimeSlot.StartTime,
                    bp.TimeSlot.EndTime,
                    bp.EffectiveFrom
                })
                .Select(g => new CurrentPriceDto
                {
                    CourtTypeId = g.Key.CourtTypeId,
                    CourtTypeName = g.First().CourtType?.Name ?? "N/A",
                    StartTime = g.Key.StartTime,
                    EndTime = g.Key.EndTime,
                    WeekdayPrice = g.FirstOrDefault(bp =>
                        bp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(bp =>
                        bp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                    EffectiveFrom = g.Key.EffectiveFrom
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();
        }
    }
}
