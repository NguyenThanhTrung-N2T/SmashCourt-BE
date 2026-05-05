namespace SmashCourt_BE.Models.Enums
{
    public enum CourtStatus
    {
        AVAILABLE = 0,
        LOCKED = 1,
        // BOOKED = 2,  // DEPRECATED - No longer used, simplified to AVAILABLE/IN_USE only
        IN_USE = 3,
        SUSPENDED = 4,
        INACTIVE = 5
    }
}
