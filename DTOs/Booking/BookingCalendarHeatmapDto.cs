using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Query parameters để lấy dữ liệu heatmap booking theo tháng.
    /// </summary>
    public class BookingCalendarHeatmapQuery
    {
        /// <summary>
        /// Năm (required, range: 1-9999).
        /// Ví dụ: 2025
        /// </summary>
        [Range(1, 9999)]
        public int Year { get; set; }

        /// <summary>
        /// Tháng (required, range: 1-12).
        /// Ví dụ: 1 (tháng 1), 12 (tháng 12)
        /// </summary>
        [Range(1, 12)]
        public int Month { get; set; }

        /// <summary>
        /// ID chi nhánh (optional).
        /// - OWNER: Có thể truyền BranchId bất kỳ hoặc null để xem tất cả
        /// - MANAGER/STAFF: BranchId sẽ bị override thành branch của mình
        /// </summary>
        public Guid? BranchId { get; set; }
    }

    /// <summary>
    /// DTO dữ liệu booking cho một ngày trong heatmap.
    /// Dùng để hiển thị calendar heatmap (giống GitHub contribution graph).
    /// </summary>
    public class BookingCalendarHeatmapDto
    {
        /// <summary>
        /// Ngày (format: yyyy-MM-dd, ví dụ: "2025-01-15").
        /// </summary>
        public string Date { get; set; } = null!;

        /// <summary>
        /// Số lượng booking trong ngày.
        /// Chỉ tính booking có status hợp lệ (không tính booking đã hủy hoặc NO_SHOW).
        /// </summary>
        public int BookingCount { get; set; }

        /// <summary>
        /// Tỷ lệ sử dụng sân (0.0 - 1.0).
        /// Ví dụ: 0.65 = 65% sân đã được đặt.
        /// 
        /// Cách tính:
        /// - dailyAvailableHours = (closeTime - openTime) × số sân
        /// - bookedHours = tổng giờ đã đặt trong ngày
        /// - occupancyRate = bookedHours / dailyAvailableHours
        /// </summary>
        public decimal OccupancyRate { get; set; }

        /// <summary>
        /// Doanh thu trong ngày (VNĐ).
        /// Chỉ tính booking đã thanh toán (PaymentStatus = PAID).
        /// </summary>
        public decimal Revenue { get; set; }
    }
}
