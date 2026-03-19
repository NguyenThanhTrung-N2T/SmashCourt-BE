namespace SmashCourt_BE.Models.Enums
{
    public enum BookingStatus
    {
        PENDING = 0,
        CONFIRMED = 1,
        PAID_ONLINE = 2,
        IN_PROGRESS = 3,
        PENDING_PAYMENT = 4,
        COMPLETED = 5,
        CANCELLED = 6,
        CANCELLED_PENDING_REFUND = 7,
        CANCELLED_REFUNDED = 8
    }
}
