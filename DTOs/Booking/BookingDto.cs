namespace SmashCourt_BE.DTOs.Booking
{
    public class BookingDto
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = null!;
        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? GuestEmail { get; set; }
        public DateOnly BookingDate { get; set; }
        public string Status { get; set; } = null!;
        public string Source { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Invoice info
        public decimal CourtFee { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal LoyaltyDiscountAmount { get; set; }
        public decimal PromotionDiscountAmount { get; set; }
        public decimal FinalTotal { get; set; }
        public string PaymentStatus { get; set; } = null!;

        // Courts
        public List<BookingCourtDto> Courts { get; set; } = [];

        // Services
        public List<BookingServiceDto> Services { get; set; } = [];
    }

    public class BookingCourtDto
    {
        public Guid CourtId { get; set; }
        public string CourtName { get; set; } = null!;
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public List<BookingPriceItemDto> PriceItems { get; set; } = [];
    }

    public class BookingPriceItemDto
    {
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Hours { get; set; }
        public decimal SubTotal { get; set; }
    }

    public class BookingServiceDto
    {
        public Guid Id { get; set; }
        public Guid ServiceId { get; set; }
        public string ServiceName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal Total { get; set; }
    }

}
