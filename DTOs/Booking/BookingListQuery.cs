using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Booking
{
    public class BookingListQuery : PaginationQuery
    {
        public Guid? BranchId { get; set; }
        public BookingStatus? Status { get; set; }
        public DateTime? Date { get; set; }
        public string? Search { get; set; } // tên khách / SĐT / mã đơn
    }
}
