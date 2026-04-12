namespace SmashCourt_BE.Jobs.Interfaces
{
    public interface IBookingJob
    {
        Task CancelExpiredPendingBookingsAsync();
        Task ProcessExpiredActiveBookingsAsync();
        Task CleanupExpiredSlotLocksAsync();
    }
}
