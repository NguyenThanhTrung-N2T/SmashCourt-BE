using SmashCourt_BE.Models.Deposit;
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

        // Đọc một lần dữ liệu booking để dựng request quyết định cọc; null khi thiếu khách hoặc hóa đơn
        Task<BookingDecisionSnapshot?> GetDecisionSnapshotAsync(Guid bookingId, CancellationToken cancellationToken = default);

        // Lõi hủy dùng chung: một lệnh UPDATE có điều kiện trạng thái ghi đủ các cột hủy; không đụng hóa đơn, Payment, khuyến mãi, sân.
        // Trả 1 = đã hủy, 0 = không còn ở trạng thái nguồn
        Task<int> CancelWithStatusCheckAsync(
            Guid bookingId,
            IReadOnlyCollection<BookingStatus> expectedStatuses,
            BookingStatus newStatus,
            CancelSourceEnum source,
            BookingCancelReason reason,
            Guid? cancelledBy,
            DateTime cancelledAtUtc);

        // ── Ghi kết quả quyết định cọc ─────────────────────────────────────────────────────────────────
        // Mỗi hàm mở đầu bằng một câu cập nhật có điều kiện trạng thái (thua ⇒ trả false, không ghi gì thêm), rồi mới ghi hóa đơn,
        // slot lock, sân của đơn. Bắt buộc chạy trong transaction của caller; mọi mốc giờ là UTC.

        // Đặt online, cọc thu qua VNPay: ghi mức cọc vào hóa đơn, đơn giữ PENDING. Trạng thái không đổi nên caller không được gọi lại
        Task<bool> ApplyPaymentRequiredAsync(
            Guid bookingId, Guid? tadssDecisionId, decimal depositRate, decimal depositAmount, DepositSource depositSource, DateTime nowUtc);

        // Đặt online, thu tại quầy: PENDING → CONFIRMED kèm token hủy, cọc 0, hóa đơn POSTPAID không hạn, xóa slot lock
        Task<bool> ConfirmWithoutOnlinePaymentAsync(
            Guid bookingId, Guid? tadssDecisionId, DepositSource depositSource, string cancelTokenHash, DateTime cancelTokenExpiresAtUtc,
            DateTime nowUtc);

        // Đặt online cần duyệt: PENDING → PENDING_APPROVAL, hóa đơn bỏ hạn và cọc, slot lock giữ tới hạn duyệt
        Task<bool> HoldForApprovalAsync(
            Guid bookingId, Guid tadssDecisionId, DateTime approvalExpiresAtUtc, string approvalReasonCode,
            IReadOnlyCollection<string> approvalReasons, DateTime nowUtc);

        // TADSS từ chối: hủy POLICY_REJECTED, nhả sân, hóa đơn EXPIRED
        Task<bool> RejectByPolicyAsync(Guid bookingId, Guid tadssDecisionId, DateTime nowUtc);

        // Hàng chờ duyệt: PENDING_APPROVAL còn hạn (branchId null = mọi chi nhánh), đơn sắp hết hạn lên đầu
        Task<PagedResult<Booking>> GetPendingApprovalsAsync(Guid? branchId, DateTime nowUtc, int page, int pageSize);

        // Ba hàm dưới chỉ thắng khi đơn còn PENDING_APPROVAL và còn hạn duyệt, kể cả khi job hết hạn duyệt chưa chạy.
        // resolvedBy null khi người duyệt không phải user của BE

        // Duyệt cọc > 0: → PENDING, cọc nguồn MANAGER, hạn thanh toán và hạn giữ chỗ mới theo window, không tạo Payment
        Task<bool> ApproveWithDepositAsync(
            Guid bookingId, Guid? resolvedBy, string note, decimal depositRate, decimal depositAmount, PaymentHoldWindow window, DateTime nowUtc);

        // Duyệt 0 %: → CONFIRMED kèm token hủy, cọc 0 nguồn MANAGER, POSTPAID, xóa slot lock
        Task<bool> ApproveWithoutDepositAsync(
            Guid bookingId, Guid? resolvedBy, string note, string cancelTokenHash, DateTime cancelTokenExpiresAtUtc, DateTime nowUtc);

        // Từ chối: hủy STAFF / APPROVAL_REJECTED, nhả sân, hóa đơn EXPIRED; đơn chờ duyệt chưa có tiền nên không hoàn tiền
        Task<bool> RejectByManagerAsync(Guid bookingId, Guid? resolvedBy, string note, DateTime nowUtc);
    }
}
