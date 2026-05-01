using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class TimeGridService : ITimeGridService
    {
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly ISlotLockRepository _slotLockRepo;
        private readonly ICourtRepository _courtRepo;

        public TimeGridService(
            ITimeSlotRepository timeSlotRepo,
            IBookingRepository bookingRepo,
            ISlotLockRepository slotLockRepo,
            ICourtRepository courtRepo)
        {
            _timeSlotRepo = timeSlotRepo;
            _bookingRepo = bookingRepo;
            _slotLockRepo = slotLockRepo;
            _courtRepo = courtRepo;
        }


        // Lấy danh sách các khung giờ của một sân trong một ngày cụ thể
        public async Task<List<TimeGridSlotDto>> GetTimeGridAsync(
            Guid branchId, Guid courtId, DateOnly date)
        {
            var court = await _courtRepo.GetByIdAsync(courtId, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            var dayType = date.DayOfWeek == DayOfWeek.Saturday ||
                          date.DayOfWeek == DayOfWeek.Sunday
                ? DayType.WEEKEND : DayType.WEEKDAY;

            // lấy tất cả các khung giờ từ cơ sở dữ liệu và lọc theo loại ngày (weekday/weekend)
            var allSlots = await _timeSlotRepo.GetAllAsync();
            var slots = allSlots
                .Where(ts => ts.DayType == dayType)
                .OrderBy(ts => ts.StartTime)
                .ToList();

            // Lấy tất cả các lock và booking của sân trong ngày đó
            var allLocks = await _slotLockRepo.GetByCourtAndDateAsync(courtId, date);
            var allBookings = await _bookingRepo.GetActiveByCourtAndDateAsync(courtId, date);

            var result = new List<TimeGridSlotDto>();

            foreach (var slot in slots)
            {
                // Check lock — in-memory overlap check
                var slotLock = allLocks.FirstOrDefault(sl =>
                    sl.StartTime < slot.EndTime && sl.EndTime > slot.StartTime);

                if (slotLock != null)
                {
                    var vnNow = DateTimeHelper.GetNowInVietnam();
                    var remainingSeconds = (int)(slotLock.ExpiresAt - vnNow).TotalSeconds;
                    result.Add(new TimeGridSlotDto
                    {
                        StartTime = slot.StartTime.ToTimeSpan(),
                        EndTime = slot.EndTime.ToTimeSpan(),
                        Status = "LOCKED",
                        LockRemainingSeconds = Math.Max(0, remainingSeconds)
                    });
                    continue;
                }

                // Check booking — in-memory overlap check
                var hasBooking = allBookings.Any(bc =>
                    bc.StartTime < slot.EndTime && bc.EndTime > slot.StartTime);

                var status = hasBooking ? "IN_USE" : "AVAILABLE";

                result.Add(new TimeGridSlotDto
                {
                    StartTime = slot.StartTime.ToTimeSpan(),
                    EndTime = slot.EndTime.ToTimeSpan(),
                    Status = status
                });
            }

            return result;
        }
    }
}
