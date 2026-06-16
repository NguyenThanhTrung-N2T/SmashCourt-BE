using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class TimeSlotService : ITimeSlotService
    {
        private readonly ITimeSlotRepository _repo;

        public TimeSlotService(ITimeSlotRepository repo)
        {
            _repo = repo;
        }

        // Lấy tất cả slot, group theo start + end để trả về 1 record duy nhất cho WEEKDAY + WEEKEND
        public async Task<List<TimeSlotDto>> GetAllAsync()
        {
            var slots = await _repo.GetAllAsync();

            // Group WEEKDAY + WEEKEND theo startTime + endTime
            return slots
                .GroupBy(ts => new { ts.StartTime, ts.EndTime })
                .Select(g => new TimeSlotDto
                {
                    WeekdaySlotId = g.First(ts => ts.DayType == DayType.WEEKDAY).Id,
                    WeekendSlotId = g.First(ts => ts.DayType == DayType.WEEKEND).Id,
                    StartTime = g.Key.StartTime.ToTimeSpan(),
                    EndTime = g.Key.EndTime.ToTimeSpan()
                })
                .OrderBy(ts => ts.StartTime)
                .ToList();
        }

        // Tạo mới sẽ tạo cả WEEKDAY + WEEKEND cùng lúc
        public async Task<TimeSlotDto> CreateAsync(CreateTimeSlotDto dto)
        {
            // 1. Convert TimeSpan → TimeOnly
            var startTime = TimeOnly.FromTimeSpan(dto.StartTime);
            var endTime = TimeOnly.FromTimeSpan(dto.EndTime);

            // 2. Validate start < end
            if (startTime >= endTime)
                throw new AppException(400,
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc", ErrorCodes.BadRequest);

            // 3. Check trùng lặp
            var existing = await _repo.GetByTimeRangeAsync(startTime, endTime);
            if (existing.Any())
                throw new AppException(409,
                    "Khung giờ này đã tồn tại", ErrorCodes.Conflict);

            // 4. Check overlap với slot khác
            var hasOverlap = await _repo.HasOverlapAsync(startTime, endTime);
            if (hasOverlap)
                throw new AppException(400,
                    "Khung giờ này bị trùng với khung giờ đã có", ErrorCodes.BadRequest);

            // 5. Tạo cả WEEKDAY + WEEKEND
            var weekday = new TimeSlot
            {
                StartTime = startTime,
                EndTime = endTime,
                DayType = DayType.WEEKDAY,
                CreatedAt = DateTime.UtcNow
            };

            var weekend = new TimeSlot
            {
                StartTime = startTime,
                EndTime = endTime,
                DayType = DayType.WEEKEND,
                CreatedAt = DateTime.UtcNow
            };

            var created = await _repo.CreateBothAsync(weekday, weekend);

            return new TimeSlotDto
            {
                WeekdaySlotId = created.First(ts => ts.DayType == DayType.WEEKDAY).Id,
                WeekendSlotId = created.First(ts => ts.DayType == DayType.WEEKEND).Id,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime
            };
        }

        // Cập nhật sẽ update cả WEEKDAY + WEEKEND cùng lúc
        public async Task<TimeSlotDto> UpdateAsync(Guid id, CreateTimeSlotDto dto)
        {
            // 1. Tìm slot theo id — có thể là WEEKDAY hoặc WEEKEND
            var slot = await _repo.GetByIdAsync(id);
            if (slot == null)
                throw new AppException(404, "Không tìm thấy khung giờ", ErrorCodes.NotFound);

            // 2. Convert TimeSpan → TimeOnly
            var startTime = TimeOnly.FromTimeSpan(dto.StartTime);
            var endTime = TimeOnly.FromTimeSpan(dto.EndTime);

            // 3. Validate start < end
            if (startTime >= endTime)
                throw new AppException(400,
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc", ErrorCodes.BadRequest);

            // 4. Lấy cả WEEKDAY + WEEKEND của slot này
            var bothSlots = await _repo.GetByTimeRangeAsync(slot.StartTime, slot.EndTime);
            var weekdaySlot = bothSlots.First(ts => ts.DayType == DayType.WEEKDAY);
            var weekendSlot = bothSlots.First(ts => ts.DayType == DayType.WEEKEND);

            // 5. Check trùng với slot khác
            var existing = await _repo.GetByTimeRangeAsync(startTime, endTime);
            if (existing.Any(ts => ts.Id != weekdaySlot.Id && ts.Id != weekendSlot.Id))
                throw new AppException(409, "Khung giờ này đã tồn tại", ErrorCodes.Conflict);

            // 6. Check overlap — bỏ qua chính nó
            var hasOverlap = await _repo.HasOverlapAsync(startTime, endTime, weekdaySlot.Id);
            if (hasOverlap)
                throw new AppException(400,
                    "Khung giờ này bị trùng với khung giờ đã có", ErrorCodes.BadRequest);

            // 7. Update cả 2
            weekdaySlot.StartTime = startTime;
            weekdaySlot.EndTime = endTime;
            weekendSlot.StartTime = startTime;
            weekendSlot.EndTime = endTime;

            await _repo.UpdateBothAsync(weekdaySlot, weekendSlot);

            return new TimeSlotDto
            {
                WeekdaySlotId = weekdaySlot.Id,
                WeekendSlotId = weekendSlot.Id,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime
            };
        }

        // Xóa sẽ xóa cả WEEKDAY + WEEKEND cùng lúc
        public async Task DeleteAsync(Guid id)
        {
            // 1. Tìm slot
            var slot = await _repo.GetByIdAsync(id);
            if (slot == null)
                throw new AppException(404, "Không tìm thấy khung giờ", ErrorCodes.NotFound);

            // 2. Check đang được dùng
            var isInUse = await _repo.IsInUseAsync(id);
            if (isInUse)
                throw new AppException(400,
                    "Khung giờ đang được sử dụng trong cấu hình giá hoặc đơn đặt sân, không thể xóa",
                    ErrorCodes.ResourceInUse);

            // 3. Xóa cả WEEKDAY + WEEKEND
            await _repo.DeleteBothAsync(slot.StartTime, slot.EndTime);
        }
    }
}
