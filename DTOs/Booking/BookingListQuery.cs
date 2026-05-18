using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Booking
{
    public class BookingListQuery : PaginationQuery
    {
        public Guid? BranchId { get; set; }
        public Guid? CourtId { get; set; }
        public BookingStatus? Status { get; set; }
        public InvoicePaymentStatus? PaymentStatus { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? CustomerKeyword { get; set; }
        public string? SortBy { get; set; } = "createdAt";
        public string? SortOrder { get; set; } = "desc";
    }
}
