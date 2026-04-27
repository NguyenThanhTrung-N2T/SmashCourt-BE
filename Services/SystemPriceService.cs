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

        // Lấy giá chung resolved cho 1 ngày cụ thể
        public async Task<List<CurrentPriceDto>> GetResolvedAsync(DateOnly date, Guid? courtTypeId = null)
        {
            var prices = await _repo.GetCurrentForDateAsync(date, courtTypeId);
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

            // 2. Convert DateTime → DateOnly
            var effectiveFromDate = DateOnly.FromDateTime(dto.EffectiveFrom);

            // 3. Validate effective_from không phải quá khứ
            var today = DateTimeHelper.GetTodayInVietnam();
            if (effectiveFromDate < today)
                throw new AppException(400,
                    "Ngày hiệu lực không thể là ngày trong quá khứ",
                    ErrorCodes.BadRequest);

            // 4. Check duplicate trong payload gửi lên
            var hasDuplicates = dto.Prices
                .GroupBy(p => new { p.StartTime, p.EndTime })
                .Any(g => g.Count() > 1);
            if (hasDuplicates)
                throw new AppException(400,
                    "Danh sách giá chứa các khung giờ bị trùng lặp", ErrorCodes.BadRequest);

            // 5. Validate + build danh sách prices
            var existingPrices = await _repo.GetExactDatePricesAsync(dto.CourtTypeId, effectiveFromDate);

            var insertPrices = new List<SystemPrice>();
            var updatePrices = new List<SystemPrice>();

            foreach (var slotPrice in dto.Prices)
            {
                // Convert TimeSpan → TimeOnly
                var startTime = TimeOnly.FromTimeSpan(slotPrice.StartTime);
                var endTime = TimeOnly.FromTimeSpan(slotPrice.EndTime);

                // Tìm WEEKDAY + WEEKEND slot theo startTime + endTime
                var slots = await _timeSlotRepo.GetByTimeRangeAsync(startTime, endTime);

                if (slots.Count != 2)
                    throw new AppException(400,
                        $"Không tìm thấy khung giờ {startTime:HH\\:mm} - {endTime:HH\\:mm}",
                        ErrorCodes.BadRequest);

                var weekdaySlot = slots.First(ts => ts.DayType == DayType.WEEKDAY);
                var weekendSlot = slots.First(ts => ts.DayType == DayType.WEEKEND);

                // Process WEEKDAY price
                var existingWeekday = existingPrices.FirstOrDefault(sp => sp.TimeSlotId == weekdaySlot.Id);
                if (existingWeekday != null)
                {
                    existingWeekday.Price = slotPrice.WeekdayPrice;
                    updatePrices.Add(existingWeekday);
                }
                else
                {
                    insertPrices.Add(new SystemPrice
                    {
                        CourtTypeId = dto.CourtTypeId,
                        TimeSlotId = weekdaySlot.Id,
                        Price = slotPrice.WeekdayPrice,
                        EffectiveFrom = effectiveFromDate,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Process WEEKEND price
                var existingWeekend = existingPrices.FirstOrDefault(sp => sp.TimeSlotId == weekendSlot.Id);
                if (existingWeekend != null)
                {
                    existingWeekend.Price = slotPrice.WeekendPrice;
                    updatePrices.Add(existingWeekend);
                }
                else
                {
                    insertPrices.Add(new SystemPrice
                    {
                        CourtTypeId = dto.CourtTypeId,
                        TimeSlotId = weekendSlot.Id,
                        Price = slotPrice.WeekendPrice,
                        EffectiveFrom = effectiveFromDate,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 6. Upsert batch
            await _repo.UpsertBatchAsync(insertPrices, updatePrices);
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
                    StartTime = g.Key.StartTime.ToTimeSpan(),
                    EndTime = g.Key.EndTime.ToTimeSpan(),
                    WeekdayPrice = g.FirstOrDefault(sp =>
                        sp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                    WeekendPrice = g.FirstOrDefault(sp =>
                        sp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0,
                    EffectiveFrom = g.Key.EffectiveFrom.ToDateTime(TimeOnly.MinValue)
                })
                .OrderBy(p => p.CourtTypeName)
                .ThenBy(p => p.StartTime)
                .ToList();
        }

        // Lấy danh sách các ngày hiệu lực (phiên bản giá) của một loại sân cụ thể
        public async Task<List<PriceVersionListDto>> GetVersionsAsync(Guid courtTypeId)
        {
            var dates = await _repo.GetVersionsAsync(courtTypeId);
            var today = DateTimeHelper.GetTodayInVietnam();

            return dates.Select(d => new PriceVersionListDto
            {
                EffectiveFrom = d.ToString("yyyy-MM-dd"),
                IsCurrent = d <= today
            }).ToList();
        }

        // Lấy chi tiết một phiên bản giá chung cho ngày hiệu lực cụ thể
        public async Task<PriceVersionDetailDto?> GetVersionDetailAsync(Guid courtTypeId, DateOnly effectiveFrom)
        {
            var prices = await _repo.GetCurrentForDateAsync(effectiveFrom, courtTypeId);

            if (!prices.Any()) return null;

            var dto = new PriceVersionDetailDto
            {
                CourtTypeId = courtTypeId,
                EffectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                Rows = prices
                    .GroupBy(sp => new { sp.TimeSlot.StartTime, sp.TimeSlot.EndTime })
                    .Select(g => new PriceVersionRowDto
                    {
                        StartTime = g.Key.StartTime.ToString("HH:mm:ss"),
                        EndTime = g.Key.EndTime.ToString("HH:mm:ss"),
                        WeekdayPrice = g.FirstOrDefault(sp => sp.TimeSlot.DayType == DayType.WEEKDAY)?.Price ?? 0,
                        WeekendPrice = g.FirstOrDefault(sp => sp.TimeSlot.DayType == DayType.WEEKEND)?.Price ?? 0
                    })
                    .OrderBy(r => r.StartTime)
                    .ToList()
            };

            return dto;
        }
    }
}
