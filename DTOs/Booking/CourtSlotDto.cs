using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class CourtSlotDto
    {
        // Bước chọn giờ của FE và độ dài mọi khung giá (time_slots) đều 30 phút; mốc lẻ chỉ tới khi gọi API trực tiếp
        public const int SlotStepMinutes = 30;

        // TADSS nhận mỗi item 30–720 phút; sân mở 06:00–22:00 nên FE kéo được tới 16 giờ, dài hơn thì TADSS từ chối và đơn bị cọc 100 %
        public const int MaxDurationMinutes = 720;

        [Required(ErrorMessage = "Vui lòng chọn sân")]
        public Guid CourtId { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        public TimeSpan StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        public TimeSpan EndTime { get; set; }

        /// <summary>
        /// Giờ bắt đầu và kết thúc đều là bội số 30 phút. Cùng điều kiện giờ bắt đầu nhỏ hơn giờ kết thúc
        /// thì đơn dài ít nhất 30 phút (TADSS từ chối item dưới 30 phút) và tiền sân là tổng giá nguyên của các khung.
        /// </summary>
        public bool IsOnSlotStep() => IsOnStep(StartTime) && IsOnStep(EndTime);

        public bool IsWithinMaxDuration() => EndTime - StartTime <= TimeSpan.FromMinutes(MaxDurationMinutes);

        private static bool IsOnStep(TimeSpan time) =>
            time.Ticks % TimeSpan.FromMinutes(SlotStepMinutes).Ticks == 0;
    }
}
