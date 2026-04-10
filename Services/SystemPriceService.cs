using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.Interfaces;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Services
{
    public class SystemPriceService : ISystemPriceService
    {
        private readonly ISystemPriceRepository _repo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly ICourtTypeRepository _courtTypeRepo;

        public SystemPriceService(
            ISystemPriceRepository repo,
            ITimeSlotRepository timeSlotRepo,
            ICourtTypeRepository courtTypeRepo)
        {
            _repo = repo;
            _timeSlotRepo = timeSlotRepo;
            _courtTypeRepo = courtTypeRepo;
        }

        // Lịch sử toàn bộ giá chung — filter theo court type nếu có
        public async Task<List<CurrentPriceDto>> GetAllAsync(Guid? courtTypeId = null)
        {
            var prices = await _repo.GetAllAsync(courtTypeId);
            return GroupPrices(prices);
        }

        // Giá chung đang có hiệu lực — filter theo court type nếu có
        public async Task<List<CurrentPriceDto>> GetCurrentAsync(Guid? courtTypeId = null)
        {
            var prices = await _repo.GetCurrentAsync(courtTypeId);
            return GroupPrices(prices);
        }

        // Tạo batch giá chung mới cho 1 court type với ngày hiệu lực cụ thể
        public async Task CreateBatchAsync(CreateSystemPriceDto dto)
        {
            // 1. Validate court type tồn tại
            var courtType = await _courtTypeRepo.GetByIdAsync(dto.CourtTypeId);
            if (courtType == null)
                throw new AppException(404,
                    "Không tìm thấy loại sân", ErrorCodes.NotFound);

            // 2. Validate effective_from không phải quá khứ
            var today = DateTimeHelper.GetTodayInVietnam();
            if (dto.EffectiveFrom < today)
                throw new AppException(400,
                    "Ngày hiệu lực không thể là ngày trong quá khứ",
                    ErrorCodes.BadRequest);

            // 3. Check duplicate trong payload gửi lên
            var hasDuplicates = dto.Prices
                .GroupBy(p => new { p.StartTime, p.EndTime })
                .Any(g => g.Count() > 1);
            if (hasDuplicates)
                throw new AppException(400,
                    "Danh sách giá chứa các khung giờ bị trùng lặp", ErrorCodes.BadRequest);

            // 3. Validate + build danh sách prices
            var systemPrices = new List<SystemPrice>();

            foreach (var slotPrice in dto.Prices)
            {
                // Tìm WEEKDAY + WEEKEND slot theo startTime + endTime
                var slots = await _timeSlotRepo.GetByTimeRangeAsync(
                    slotPrice.StartTime, slotPrice.EndTime);

                if (slots.Count != 2)
                    throw new AppException(400,
                        $"Không tìm thấy khung giờ {slotPrice.StartTime:HH\\:mm} - {slotPrice.EndTime:HH\\:mm}",
                        ErrorCodes.BadRequest);

                var weekdaySlot = slots.First(ts => ts.DayType == DayType.WEEKDAY);
                var weekendSlot = slots.First(ts => ts.DayType == DayType.WEEKEND);

                // Check trùng effective_from
                var weekdayExists = await _repo.ExistsAsync(
                    dto.CourtTypeId, weekdaySlot.Id, dto.EffectiveFrom);
                var weekendExists = await _repo.ExistsAsync(
                    dto.CourtTypeId, weekendSlot.Id, dto.EffectiveFrom);

                if (weekdayExists || weekendExists)
                    throw new AppException(409,
                        $"Đã tồn tại cấu hình giá cho khung giờ {slotPrice.StartTime:HH\\:mm} - {slotPrice.EndTime:HH\\:mm} với ngày hiệu lực này",
                        ErrorCodes.Conflict);

                systemPrices.Add(new SystemPrice
                {
                    CourtTypeId = dto.CourtTypeId,
                    TimeSlotId = weekdaySlot.Id,
                    Price = slotPrice.WeekdayPrice,
                    EffectiveFrom = dto.EffectiveFrom,
                    CreatedAt = DateTime.UtcNow
                });

                systemPrices.Add(new SystemPrice
                {
                    CourtTypeId = dto.CourtTypeId,
                    TimeSlotId = weekendSlot.Id,
                    Price = slotPrice.WeekendPrice,
                    EffectiveFrom = dto.EffectiveFrom,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 4. Insert batch
            await _repo.CreateBatchAsync(systemPrices);
        }

        // Group WEEKDAY + WEEKEND vào 1 dòng
        private static List<CurrentPriceDto> GroupPrices(List<SystemPrice> prices)
        {
            return prices
                .GroupBy(sp => new
                {
                    sp.CourtTypeId,
                    sp.TimeSlot.StartTime,
                    sp.TimeSlot.EndTime,
                    sp.EffectiveFrom
                })
                .Select(g => new CurrentPriceDto
                {
                    CourtTypeId = g.Key.CourtTypeId,
                    CourtTypeName = g.First().CourtType?.Name ?? "N/A",
                    StartTime = g.Key.StartTime,
                    EndTime = g.Key.EndTime,
                    WeekdayPrice = g.FirstOrDefault(sp =>
                        sp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(sp =>
                        sp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                    EffectiveFrom = g.Key.EffectiveFrom
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();
        }
    }
}
