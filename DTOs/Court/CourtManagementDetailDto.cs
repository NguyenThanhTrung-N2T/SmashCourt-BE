using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Court
{
    /// <summary>
    /// Chi tiết sân trong modal "Xem chi tiết sân" của màn hình quản lý
    /// </summary>
    public class CourtManagementDetailDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public CourtOperationalStatus OperationalStatus { get; set; } = CourtOperationalStatus.READY;
        public string TypeName { get; set; } = null!;

        /// <summary>Cấu hình giá sân</summary>
        public CourtPriceConfigDto Prices { get; set; } = null!;

        /// <summary>Khách đang chơi ngay lúc này (null nếu không có ai)</summary>
        public CurrentPlayerDto? CurrentPlayer { get; set; }

        /// <summary>Tổng số lượt đặt trong ngày được query</summary>
        public int BookingsCount { get; set; }

        /// <summary>Các booking sắp tới trong ngày hôm nay</summary>
        public List<UpcomingBookingDto> UpcomingBookings { get; set; } = [];
    }

    public class CourtPriceConfigDto
    {
        public decimal? NormalPrice { get; set; }
        public decimal? PeakPrice { get; set; }
    }

    public class CurrentPlayerDto
    {
        public string Name { get; set; } = null!;
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
    }

    public class UpcomingBookingDto
    {
        public Guid BookingId { get; set; }
        public string TimeRange { get; set; } = null!;
        public string PlayerName { get; set; } = null!;

        /// <summary>Trạng thái booking (Confirmed / Paid...)</summary>
        public string Status { get; set; } = null!;

        /// <summary>XN = Đã Xác Nhận, TT = Đã Thanh Toán, etc.</summary>
        public string StatusShort { get; set; } = null!;
    }
}
