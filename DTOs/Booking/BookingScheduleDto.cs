namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Query parameters để lấy lịch booking theo sân trong một ngày.
    /// </summary>
    public class BookingScheduleQuery
    {
        /// <summary>
        /// ID chi nhánh (optional).
        /// - OWNER: Có thể truyền BranchId bất kỳ hoặc null để xem tất cả
        /// - MANAGER/STAFF: BranchId sẽ bị override thành branch của mình
        /// </summary>
        public Guid? BranchId { get; set; }

        /// <summary>
        /// Ngày cần xem lịch (required).
        /// Format: yyyy-MM-dd hoặc yyyy-MM-ddTHH:mm:ss
        /// </summary>
        public DateTime Date { get; set; }
    }

    /// <summary>
    /// DTO trả về lịch booking của một sân trong ngày.
    /// Mỗi sân có danh sách booking tương ứng.
    /// </summary>
    public class BookingScheduleCourtDto
    {
        /// <summary>
        /// ID của sân.
        /// </summary>
        public Guid CourtId { get; set; }

        /// <summary>
        /// Tên sân (ví dụ: "Sân 1", "Sân 2").
        /// </summary>
        public string CourtName { get; set; } = null!;

        /// <summary>
        /// Danh sách booking của sân trong ngày.
        /// Nếu sân không có booking nào thì list rỗng [].
        /// </summary>
        public List<BookingScheduleItemDto> Bookings { get; set; } = [];
    }

    /// <summary>
    /// DTO chi tiết một booking trong lịch sân.
    /// </summary>
    public class BookingScheduleItemDto
    {
        /// <summary>
        /// ID của booking.
        /// </summary>
        public Guid BookingId { get; set; }

        /// <summary>
        /// Thời gian bắt đầu (format: HH:mm, ví dụ: "08:00").
        /// </summary>
        public string StartTime { get; set; } = null!;

        /// <summary>
        /// Thời gian kết thúc (format: HH:mm, ví dụ: "10:00").
        /// </summary>
        public string EndTime { get; set; } = null!;

        /// <summary>
        /// Trạng thái booking (PENDING, CONFIRMED, PAID_ONLINE, IN_PROGRESS, PENDING_PAYMENT).
        /// </summary>
        public string Status { get; set; } = null!;
    }
}
