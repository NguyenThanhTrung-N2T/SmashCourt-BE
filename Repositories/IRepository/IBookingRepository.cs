using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBookingRepository
    {
        
        // Lấy danh sách booking để hiển thị cho staff (có filter, phân trang)
        Task<PagedResult<Booking>> GetAllAsync(BookingListQuery query, string userRole, Guid userId);

        Task<List<BookingScheduleCourtDto>> GetScheduleAsync(BookingScheduleQuery query, string userRole, Guid userId);

        Task<BookingDashboardSummaryDto> GetDashboardSummaryAsync(BookingDashboardSummaryQuery query, string userRole, Guid userId);

        Task<List<BookingCalendarHeatmapDto>> GetCalendarHeatmapAsync(BookingCalendarHeatmapQuery query, string userRole, Guid userId);

        // Lấy danh sách booking của khách hàng (có phân trang và filter)
        Task<PagedResult<Booking>> GetByCustomerIdAsync(Guid customerId, BookingListQuery query);

        // Lấy thông tin booking theo id, có phân quyền
        Task<Booking?> GetByIdWithDetailsAsync(Guid id);

        // Lấy booking status (lightweight query - chỉ lấy status)
        Task<BookingStatus> GetBookingStatusAsync(Guid id);

        // Lấy thông tin booking theo token hủy (dùng cho khách hàng hủy booking online)
        Task<Booking?> GetByCancelTokenAsync(string tokenHash);

        // Kiểm tra xem có booking nào đã tồn tại trên sân vào khung giờ đó hay không (dùng để validate khi tạo hoặc cập nhật booking)
        Task<bool> HasOverlapAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        // Lấy danh sách booking court đang active (đang check-in) của một sân trong một ngày cụ thể
        Task<List<BookingCourt>> GetActiveByCourtAndDateAsync(Guid courtId, DateOnly date);

        // Tạo mới booking, trả về booking đã được tạo (có id)
        Task<Booking> CreateAsync(Booking booking);

        // Cập nhật booking
        Task UpdateAsync(Booking booking);

        // Thêm mới booking court, trả về booking court đã được tạo (có id)
        Task<BookingCourt> AddCourtAsync(BookingCourt bookingCourt);

        // Thêm price item cho slot đặt sân trong booking court
        Task AddPriceItemsAsync(List<BookingPriceItem> items);

        // Thêm promotion vào booking
        Task AddPromotionAsync(BookingPromotion promotion);

        // Thêm dịch vụ vào booking
        Task AddServiceAsync(BookingService service);

        // Cập nhật dịch vụ trong booking (quantity, price, etc.)
        Task UpdateServiceAsync(BookingService service);

        // Cập nhật quantity của service một cách atomic (tránh race condition)
        Task<int> UpdateServiceQuantityAtomicAsync(Guid serviceId, int quantityToAdd);

        // Tính tổng service fee của booking (query từ DB, không dùng memory)
        Task<decimal> CalculateServiceFeeAsync(Guid bookingId);

        // Xóa dịch vụ khỏi booking
        Task RemoveServiceAsync(BookingService service);

        // Cập nhật trạng thái active của booking court (dùng để check-in/check-out)
        Task UpdateCourtActiveStatusAsync(Guid bookingId, bool isActive);

        // Atomic consume token để tránh race condition khi hủy booking qua link
        Task<bool> TryConsumeTokenAsync(Guid bookingId, string tokenHash, DateTime consumedAt);

        /// <summary>
        /// Cập nhật booking status với conditional update để tránh race condition
        /// </summary>
        /// <param name="bookingId">ID của booking</param>
        /// <param name="newStatus">Status mới</param>
        /// <param name="expectedStatus">Status mong đợi (status cũ)</param>
        /// <returns>Số rows affected (1 = thành công, 0 = conflict)</returns>
        Task<int> UpdateWithStatusCheckAsync(Guid bookingId, BookingStatus newStatus, BookingStatus expectedStatus);

        /// <summary>
        /// Atomic update booking khi thanh toán thành công (VNPay IPN)
        /// Update tất cả fields trong 1 operation duy nhất để tránh race condition
        /// </summary>
        /// <param name="bookingId">ID của booking</param>
        /// <param name="expectedStatus">Status mong đợi (PENDING)</param>
        /// <param name="cancelTokenHash">Cancel token hash</param>
        /// <param name="cancelTokenExpiry">Cancel token expiry</param>
        /// <param name="now">Timestamp hiện tại</param>
        /// <returns>Số rows affected (1 = thành công, 0 = conflict)</returns>
        Task<int> AtomicUpdatePaymentSuccessAsync(
            Guid bookingId,
            BookingStatus expectedStatus,
            string cancelTokenHash,
            DateTime cancelTokenExpiry,
            DateTime now);

        /// <summary>
        /// Lấy số lượng booking đã hoàn thành của khách hàng (dùng cho promotion condition)
        /// </summary>
        /// <param name="customerId">ID của khách hàng</param>
        /// <returns>Số lượng booking đã hoàn thành</returns>
        Task<int> GetCompletedBookingCountAsync(Guid customerId);
        Task SetActualEndPlayTimeAsync(Guid bookingId, TimeOnly actualEnd);

        /// <summary>
        /// Đọc một lần mọi dữ liệu của booking để dựng request quyết định cọc TADSS (chỉ đọc).
        /// Trả null khi booking không tồn tại, không có khách hoặc chưa có hóa đơn (không đủ dữ liệu dựng request).
        /// </summary>
        Task<BookingDecisionSnapshot?> GetDecisionSnapshotAsync(Guid bookingId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lõi hủy dùng chung (P6-14): một lệnh UPDATE có điều kiện trạng thái, ghi đủ status, cancelled_at,
        /// cancel_source, cancel_reason, cancelled_by, updated_at. Không đụng hóa đơn, Payment, khuyến mãi, sân.
        /// Bỏ qua change tracker: entity đang được track phải đọc lại sau khi gọi.
        /// </summary>
        /// <param name="expectedStatuses">Trạng thái nguồn được phép (không rỗng)</param>
        /// <param name="newStatus">Một trong các trạng thái hủy</param>
        /// <param name="cancelledAtUtc">Mốc hủy, bắt buộc Kind = Utc</param>
        /// <returns>1 = đã hủy, 0 = booking không tồn tại hoặc không còn ở trạng thái nguồn</returns>
        Task<int> CancelWithStatusCheckAsync(
            Guid bookingId,
            IReadOnlyCollection<BookingStatus> expectedStatuses,
            BookingStatus newStatus,
            CancelSourceEnum source,
            BookingCancelReason reason,
            Guid? cancelledBy,
            DateTime cancelledAtUtc);

        // ── Transaction 2 của đặt sân online (P6-11, P6-14, spec 7.6) ──────────────────────────────────
        // Mỗi hàm mở đầu bằng câu cổng có điều kiện status = PENDING trên booking; thua cổng ⇒ trả false và
        // không ghi gì thêm. Thắng ⇒ ghi tiếp hóa đơn, slot lock, sân của chính đơn đó. Phải gọi trong
        // transaction của caller (TransactionScope) để lỗi giữa chừng rollback toàn bộ; không có transaction thì ném InvalidOperationException
        // trước khi ghi. Không đụng hold_deadline.
        // Lưu ý nhánh a/d: câu cổng không đổi trạng thái (PENDING vẫn PENDING) nên gọi lần hai vẫn "thắng" và ghi đè mức cọc. Chấp nhận được vì
        // transaction 2 chỉ chạy một lần mỗi request; caller không được gọi lại hàm này để "thử lại" cho cùng một đơn.
        // Khuyến mãi, Payment, VNPay do service xử lý sau khi hàm trả true.

        /// <summary>Nhánh a (TADSS) và d (dự phòng 100 %): ghi mức cọc vào hóa đơn; đơn giữ PENDING và giữ slot lock.</summary>
        Task<bool> ApplyPaymentRequiredAsync(
            Guid bookingId, Guid? tadssDecisionId, decimal depositRate, decimal depositAmount, DepositSource depositSource, DateTime nowUtc);

        /// <summary>Nhánh a′: PENDING → CONFIRMED, cọc áp dụng 0, hóa đơn POSTPAID không hạn, xóa slot lock.</summary>
        Task<bool> ConfirmWithoutOnlinePaymentAsync(Guid bookingId, Guid? tadssDecisionId, DepositSource depositSource, DateTime nowUtc);

        /// <summary>Nhánh b: PENDING → PENDING_APPROVAL, hóa đơn bỏ hạn và cọc, slot lock gia hạn tới hạn duyệt.</summary>
        Task<bool> HoldForApprovalAsync(
            Guid bookingId, Guid tadssDecisionId, DateTime approvalExpiresAtUtc, string approvalReasonCode,
            IReadOnlyCollection<string> approvalReasons, DateTime nowUtc);

        /// <summary>Nhánh c: hủy POLICY_REJECTED bằng lõi hủy, ghi decision id, nhả sân, hóa đơn EXPIRED, xóa slot lock.</summary>
        Task<bool> RejectByPolicyAsync(Guid bookingId, Guid tadssDecisionId, DateTime nowUtc);
    }
}
