using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Court
{
    /// <summary>
    /// Kết quả trả về cho màn hình quản lý sân (dashboard tổng quan + danh sách sân với lịch hôm nay)
    /// </summary>
    public class CourtManagementDashboardDto
    {
        /// <summary>4 ô thống kê góc trên của màn hình</summary>
        public CourtManagementStatsDto Stats { get; set; } = null!;

        /// <summary>Danh sách card sân với lịch hôm nay</summary>
        public List<CourtManagementCardDto> Courts { get; set; } = [];
    }

    public class CourtManagementStatsDto
    {
        /// <summary>The date these stats reflect (yyyy-MM-dd)</summary>
        public DateOnly Date { get; set; }
        public int Ready { get; set; }
        public int Booked { get; set; }
        public int Playing { get; set; }
        public int Suspended { get; set; }
        public int Total { get; set; }
    }

    /// <summary>
    /// Mỗi card sân trên màn hình quản lý sân
    /// </summary>
    public class CourtManagementCardDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string TypeName { get; set; } = null!;

        /// <summary>Trạng thái thực tế (derived)</summary>
        public CourtOperationalStatus OperationalStatus { get; set; } = CourtOperationalStatus.READY;

        /// <summary>Số lượt đặt trong ngày được query</summary>
        public int BookingsCount { get; set; }

        /// <summary>Giá niêm yết cơ bản (giờ thường)</summary>
        public decimal? BasePrice { get; set; }

        /// <summary>Timeline các khung giờ hoạt động trong ngày</summary>
        public List<CourtTimelineSlotDto> ScheduleTimeline { get; set; } = [];
    }

    /// <summary>
    /// Mỗi ô trong lịch trực quan của court card (hiển thị dạng màu sắc)
    /// </summary>
    public class CourtTimelineSlotDto
    {
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
        public bool IsEarlyCheckout { get; set; }

        /// <summary>Available / Booked / Playing</summary>
        public CourtTimelineSlotStatus Status { get; set; } = CourtTimelineSlotStatus.AVAILABLE;
    }

    /// <summary>
    /// Query params cho management dashboard
    /// </summary>
    public class CourtManagementDashboardQuery
    {
        /// <summary>Optional: OWNER có thể truyền để xem chi nhánh cụ thể. MANAGER/STAFF tự động resolve.</summary>
        public Guid? BranchId { get; set; }

        /// <summary>Ngày cần query (yyyy-MM-dd). Mặc định là hôm nay.</summary>
        public DateOnly? Date { get; set; }

        /// <summary>Tìm theo tên sân</summary>
        public string? Search { get; set; }

        /// <summary>Lọc theo loại sân</summary>
        public Guid? TypeId { get; set; }

        /// <summary>Số trang (bắt đầu từ 1). Mặc định 1.</summary>
        public int Page { get; set; } = 1;

        /// <summary>Số sân mỗi trang. Mặc định 20.</summary>
        public int PageSize { get; set; } = 20;
    }
}
