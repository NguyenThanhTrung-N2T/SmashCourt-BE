using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Booking
    {
        public Guid Id { get; set; }
        public string BookingCode { get; set; } = null!;
        public Guid BranchId { get; set; }
        public Guid? CustomerId { get; set; }
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? GuestEmail { get; set; }
        public DateOnly BookingDate { get; set; }
        public BookingStatus Status { get; set; } = BookingStatus.PENDING;
        public BookingSource Source { get; set; }
        public string? Note { get; set; }
        public DateTime? CheckedInAt { get; set; }  // Thời điểm khách check-in (bắt đầu chơi)
        public Guid? CreatedBy { get; set; }
        public Guid? CancelledBy { get; set; }
        public DateTime? CancelledAt { get; set; }
        public CancelSourceEnum? CancelSource { get; set; }
        public string? CancelTokenHash { get; set; }
        public DateTime? CancelTokenExpiresAt { get; set; }
        public DateTime? CancelTokenUsedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Phase 6 — cọc TADSS (đơn tạo trước Phase 6 hoặc không qua TADSS: tất cả NULL)
        public Guid? TadssDecisionId { get; set; }
        public DateTime? ApprovalExpiresAt { get; set; }            // Hạn Manager duyệt; giữ lại sau khi duyệt/hủy để tra cứu
        public string? ApprovalReasonCode { get; set; }
        public List<string>? ApprovalReasons { get; set; }          // Lý do cần duyệt (text[])
        public ApprovalResult? ApprovalResult { get; set; }
        public Guid? ApprovalResolvedBy { get; set; }
        public DateTime? ApprovalResolvedAt { get; set; }
        public string? ApprovalResolutionNote { get; set; }
        public BookingCancelReason? CancelReason { get; set; }
        public DateTime? TadssOutcomeReportedAt { get; set; }      // Đã báo kết quả booking về TADSS

        // Navigation
        public Branch Branch { get; set; } = null!;
        public User? Customer { get; set; }
        public User? CreatedByUser { get; set; }
        public User? CancelledByUser { get; set; }
        public ICollection<BookingCourt> BookingCourts { get; set; } = [];
        public ICollection<BookingService> BookingServices { get; set; } = [];
        public BookingPromotion? BookingPromotion { get; set; }
        public ICollection<SlotLock> SlotLocks { get; set; } = [];  // Một lock cho mỗi sân của đơn (P6-13)
        public Invoice? Invoice { get; set; }
        public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = [];
    }
}
