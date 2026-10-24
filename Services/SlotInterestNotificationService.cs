using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    /// <summary>
    /// Báo người chờ slot khi slot được nhả; dùng chung cho mọi đường nhả slot (các đường hủy, từ chối duyệt).
    /// </summary>
    public class SlotInterestNotificationService : ISlotInterestNotificationService
    {
        private readonly ISlotInterestRepository _slotInterestRepo;
        private readonly EmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SlotInterestNotificationService> _logger;

        public SlotInterestNotificationService(
            ISlotInterestRepository slotInterestRepo,
            EmailService emailService,
            IConfiguration configuration,
            ILogger<SlotInterestNotificationService> logger)
        {
            _slotInterestRepo = slotInterestRepo;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Gửi thông báo email cho tất cả người đã đăng ký interest cho các slot vừa được giải phóng.
        /// Gọi SAU KHI commit transaction cancel — đảm bảo không gửi email khi DB chưa thực sự lưu.
        /// Dùng pattern chung cho mọi nguồn hủy: Staff, Customer, Token, Quản lý từ chối.
        /// </summary>
        public async Task NotifyReleasedSlotsAsync(Booking booking)
        {
            var frontendUrl = _configuration["FrontendBaseUrl"] ?? "https://smashcourt.vn";
            var branchName = booking.Branch?.Name ?? string.Empty;

            foreach (var bc in booking.BookingCourts)
            {
                var interested = await _slotInterestRepo.GetOverlappingSlotInterestsAsync(
                    bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                if (!interested.Any()) continue;

                _logger.LogInformation(
                    "[SLOT_INTEREST] Notifying {Count} users for released slot | Court={CourtId} | Date={Date} | Slot={Start}-{End}",
                    interested.Count, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                var courtName = bc.Court?.Name ?? "Sân";
                var bookingUrl = frontendUrl;

                foreach (var interest in interested)
                {
                    try
                    {
                        await _emailService.SendSlotAvailableNotificationAsync(
                            interest.Email,
                            courtName,
                            branchName,
                            bc.Date,
                            bc.StartTime,
                            bc.EndTime,
                            bookingUrl);
                    }
                    catch (Exception ex)
                    {
                        // Lỗi gửi email không block việc notify người khác
                        _logger.LogError(ex,
                            "[SLOT_INTEREST] Failed to send notification to {Email} for slot Court={CourtId}",
                            interest.Email, bc.CourtId);
                    }
                }

                // Xóa tất cả interests của slot này sau khi đã notify (one-shot)
                var deletedCount = await _slotInterestRepo.DeleteOverlappingSlotInterestsAsync(
                    bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                _logger.LogInformation(
                    "[SLOT_INTEREST] Deleted {DeletedCount} slot interests after notification | Court={CourtId} | Date={Date} | Slot={Start}-{End}",
                    deletedCount, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);
            }
        }
    }
}
