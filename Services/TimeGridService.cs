using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
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

        public async Task<List<TimeGridSlotDto>> GetTimeGridAsync(
            Guid branchId, Guid courtId, DateOnly date)
        {
            var court = await _courtRepo.GetByIdAsync(courtId, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            var dayType = date.DayOfWeek == DayOfWeek.Saturday ||
                          date.DayOfWeek == DayOfWeek.Sunday
                ? DayType.WEEKEND : DayType.WEEKDAY;

            var allSlots = await _timeSlotRepo.GetAllAsync();
            var slots = allSlots
                .Where(ts => ts.DayType == dayType)
                .OrderBy(ts => ts.StartTime)
                .ToList();

            var result = new List<TimeGridSlotDto>();

            foreach (var slot in slots)
            {
                // Check slot_lock
                var slotLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                    courtId, date, slot.StartTime, slot.EndTime);

                if (slotLock != null)
                {
                    var remainingSeconds = (int)(slotLock.ExpiresAt - DateTime.UtcNow).TotalSeconds;
                    result.Add(new TimeGridSlotDto
                    {
                        StartTime = slot.StartTime,
                        EndTime = slot.EndTime,
                        Status = "LOCKED",
                        LockRemainingSeconds = Math.Max(0, remainingSeconds)
                    });
                    continue;
                }

                // Check booking
                var hasBooking = await _bookingRepo.HasOverlapAsync(
                    courtId, date, slot.StartTime, slot.EndTime);

                var status = hasBooking
                    ? court.Status == CourtStatus.IN_USE ? "IN_USE" : "BOOKED"
                    : "AVAILABLE";

                result.Add(new TimeGridSlotDto
                {
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    Status = status
                });
            }

            return result;
        }
    }
}
