namespace SmashCourt_BE.Models.Enums
{
    /// <summary>
    /// Derived operational status used in management UI (card-level)
    /// </summary>
    public enum CourtOperationalStatus
    {
        READY = 0,
        BOOKED = 1,
        PLAYING = 2,
        SUSPENDED = 3
    }
}
