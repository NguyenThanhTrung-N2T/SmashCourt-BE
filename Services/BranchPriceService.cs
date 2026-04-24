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

        // Lấy tất cả cấu hình giá override của chi nhánh, có thể filter theo courtTypeId
        public async Task<List<CurrentPriceDto>> GetAllAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            await ValidateBranchAsync(branchId);
            var prices = await _repo.GetAllAsync(branchId, courtTypeId);
            return GroupPrices(prices);
        }

        // Lấy cấu hình giá effective hiện tại (dựa trên ngày hôm nay) của chi nhánh, có thể filter theo courtTypeId
        public async Task<List<EffectivePriceDto>> GetEffectiveCurrentAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            await ValidateBranchAsync(branchId);

            var branchPrices = await _repo.GetCurrentAsync(branchId, courtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentAsync(courtTypeId);

            // Index branch overrides theo (CourtTypeId, StartTime, EndTime)
            var branchDict = branchPrices
                .GroupBy(bp => new
                {
                    bp.CourtTypeId,
                    bp.TimeSlot.StartTime,
                    bp.TimeSlot.EndTime
                })
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        WeekdayPrice = g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                        WeekendPrice = g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                        EffectiveFrom = g.First().EffectiveFrom
                    });

            // Merge: với mỗi slot trong system price, dùng branch override nếu có
            var result = systemPrices
                .GroupBy(sp => new
                {
                    sp.CourtTypeId,
                    sp.TimeSlot.StartTime,
                    sp.TimeSlot.EndTime
                })
                .Select(g =>
                {
                    var key = g.Key;
                    var hasBranch = branchDict.TryGetValue(key, out var branch);

                    return new EffectivePriceDto
                    {
                        CourtTypeId      = key.CourtTypeId,
                        CourtTypeName    = g.First().CourtType?.Name ?? "N/A",
                        StartTime        = key.StartTime.ToTimeSpan(),
                        EndTime          = key.EndTime.ToTimeSpan(),
                        WeekdayPrice     = hasBranch
                            ? branch!.WeekdayPrice
                            : (g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0),
                        WeekendPrice     = hasBranch
                            ? branch!.WeekendPrice
                            : (g.FirstOrDefault(x => x.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0),
                        EffectiveFrom    = (hasBranch ? branch!.EffectiveFrom : g.First().EffectiveFrom).ToDateTime(TimeOnly.MinValue),
                        PriceSource      = hasBranch ? "BRANCH_OVERRIDE" : "SYSTEM_PRICE"
                    };
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();

            return result;
        }

        // Tạo mới 1 batch giá override cho 1 court type tại chi nhánh, có thể tạo nhiều khung giờ trong cùng 1 request
        public async Task CreateBatchAsync(Guid branchId, CreateBranchPriceDto dto)
        {
            // 1. Validate branch
            await ValidateBranchAsync(branchId);

            // 2. Convert DateTime → DateOnly
            var effectiveFromDate = DateOnly.FromDateTime(dto.EffectiveFrom);

            // 3. Validate effective_from
            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFromDate < today)
                throw new AppException(400,
                    "Ngày hiệu lực không thể là ngày trong quá khứ",
                    ErrorCodes.BadRequest);

            // 4. Validate court type belongs to branch
            var isCourtTypeEnabled = await _branchRepo.IsCourtTypeEnabledAsync(branchId, dto.CourtTypeId);
            if (!isCourtTypeEnabled)
                throw new AppException(400,
                    "Loại sân không hợp lệ hoặc không thuộc chi nhánh này",
                    ErrorCodes.BadRequest);

            // 5. Check duplicate trong payload gửi lên
            var hasDuplicates = dto.Prices
                .GroupBy(p => new { p.StartTime, p.EndTime })
                .Any(g => g.Count() > 1);
            if (hasDuplicates)
                throw new AppException(400,
                    "Danh sách giá chứa các khung giờ bị trùng lặp", ErrorCodes.BadRequest);

            // 6. Build danh sách prices
            var overrides = new List<BranchPriceOverride>();

            foreach (var slotPrice in dto.Prices)
            {
                // Convert TimeSpan → TimeOnly
                var startTime = TimeOnly.FromTimeSpan(slotPrice.StartTime);
                var endTime = TimeOnly.FromTimeSpan(slotPrice.EndTime);

                var slots = await _timeSlotRepo.GetByTimeRangeAsync(startTime, endTime);

                if (slots.Count != 2)
                    throw new AppException(400,
                        $"Khung giờ {startTime:HH\\:mm} - {endTime:HH\\:mm} không hợp lệ",
                        ErrorCodes.BadRequest);

                var weekdaySlot = slots.First(ts => ts.DayType == DayType.WEEKDAY);
                var weekendSlot = slots.First(ts => ts.DayType == DayType.WEEKEND);

                // Check trùng
                if (await _repo.ExistsAsync(branchId, dto.CourtTypeId, weekdaySlot.Id, effectiveFromDate) ||
                    await _repo.ExistsAsync(branchId, dto.CourtTypeId, weekendSlot.Id, effectiveFromDate))
                    throw new AppException(409,
                        $"Đã tồn tại giá override cho khung giờ {startTime:HH\\:mm} - {endTime:HH\\:mm}",
                        ErrorCodes.Conflict);

                overrides.Add(new BranchPriceOverride
                {
                    BranchId = branchId,
                    CourtTypeId = dto.CourtTypeId,
                    TimeSlotId = weekdaySlot.Id,
                    Price = slotPrice.WeekdayPrice,
                    EffectiveFrom = effectiveFromDate,
                    CreatedAt = DateTime.UtcNow
                });

                overrides.Add(new BranchPriceOverride
                {
                    BranchId = branchId,
                    CourtTypeId = dto.CourtTypeId,
                    TimeSlotId = weekendSlot.Id,
                    Price = slotPrice.WeekendPrice,
                    EffectiveFrom = effectiveFromDate,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _repo.CreateBatchAsync(overrides);
        }

        // Xóa 1 cấu hình giá override dựa trên courtTypeId + time slot + effectiveFrom. Chỉ xóa được với cấu hình giá chưa có hiệu lực (ngày hiệu lực > ngày hôm nay)
        public async Task DeleteAsync(Guid branchId, DeleteBranchPriceDto dto)
        {
            await ValidateBranchAsync(branchId);

            // Convert DateTime → DateOnly và TimeSpan → TimeOnly
            var effectiveFromDate = DateOnly.FromDateTime(dto.EffectiveFrom);
            var startTime = TimeOnly.FromTimeSpan(dto.StartTime);
            var endTime = TimeOnly.FromTimeSpan(dto.EndTime);

            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFromDate <= today)
                throw new AppException(400,
                    "Không thể xóa cấu hình giá đã hoặc đang có hiệu lực", ErrorCodes.BadRequest);

            var deleted = await _repo.DeletePairAsync(
                branchId,
                dto.CourtTypeId,
                effectiveFromDate,
                startTime,
                endTime);

            if (deleted == 0)
                throw new AppException(404,
                    "Không tìm thấy cấu hình giá", ErrorCodes.NotFound);
        }

        // Tính giá thuê sân dựa trên thời gian đặt sân (startTime, endTime) + ngày đặt sân (bookingDate) + loại sân (courtTypeId). Lấy giá theo ngày bookingDate thay vì ngày hôm nay để tránh lỗi giá tương lai
        public async Task<CalculatePriceResultDto> CalculateAsync(
            Guid branchId, CalculatePriceDto dto)
        {
            // 1. Convert TimeSpan → TimeOnly và DateTime → DateOnly
            var startTime = TimeOnly.FromTimeSpan(dto.StartTime);
            var endTime = TimeOnly.FromTimeSpan(dto.EndTime);
            var bookingDate = DateOnly.FromDateTime(dto.BookingDate);

            // 2. Validate
            if (startTime >= endTime)
                throw new AppException(400,
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc", ErrorCodes.BadRequest);
            // Booking date không được là ngày trong quá khứ            
            var today = DateTimeHelper.GetTodayInVietnam();
            if (bookingDate < today)
                throw new AppException(400,
                    "Không thể tính giá cho ngày trong quá khứ", ErrorCodes.BadRequest);

            // 3. Validate branch + tìm court → lấy courtTypeId
            await ValidateBranchAsync(branchId);

            var court = await _courtRepo.GetByIdAsync(dto.CourtId, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);
            if (court.Status == CourtStatus.SUSPENDED || court.Status == CourtStatus.LOCKED)
                throw new AppException(400, "Sân hiện đang bị khóa hoặc bảo trì", ErrorCodes.BadRequest);

            // 4. Xác định WEEKDAY / WEEKEND
            var dayOfWeek = dto.BookingDate.DayOfWeek;
            var dayType = (dayOfWeek == DayOfWeek.Saturday ||
                           dayOfWeek == DayOfWeek.Sunday)
                ? DayType.WEEKEND
                : DayType.WEEKDAY;

            // 5. Lấy time slots theo dayType tại DB — sort theo startTime
            var relevantSlots = await _timeSlotRepo.GetByDayTypeAsync(dayType);

            if (!relevantSlots.Any())
                throw new AppException(400,
                    "Chưa cấu hình khung giờ cho hệ thống", ErrorCodes.BadRequest);

            // 6. Lấy giá dựa trên ngày BookingDate thay vì ngày hôm nay (tránh lỗi giá tương lai)
            var branchPrices = await _repo.GetCurrentForDateAsync(branchId, bookingDate, court.CourtTypeId);
            var systemPrices = await _systemPriceRepo.GetCurrentForDateAsync(bookingDate, court.CourtTypeId);

            // 7. Chia sub-slot + tính tiền
            var breakdown = new List<PriceBreakdownDto>();
            decimal courtFee = 0;

            foreach (var slot in relevantSlots)
            {
                // Tìm overlap giữa slot và range đặt sân (dùng startTime/endTime đã convert)
                var overlapStart = slot.StartTime > startTime
                    ? slot.StartTime
                    : startTime;
                var overlapEnd = slot.EndTime < endTime
                    ? slot.EndTime
                    : endTime;

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
                    StartTime = overlapStart.ToTimeSpan(),
                    EndTime = overlapEnd.ToTimeSpan(),
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

        // Validate branch tồn tại
        private async Task ValidateBranchAsync(Guid branchId)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
        }

        // Group giá override theo courtTypeId + time slot để trả về dạng dễ đọc cho frontend (1 record sẽ có weekdayPrice + weekendPrice)
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
                    StartTime = g.Key.StartTime.ToTimeSpan(),
                    EndTime = g.Key.EndTime.ToTimeSpan(),
                    WeekdayPrice = g.FirstOrDefault(bp =>
                        bp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(bp =>
                        bp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                    EffectiveFrom = g.Key.EffectiveFrom.ToDateTime(TimeOnly.MinValue)
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();
        }
    }
}
