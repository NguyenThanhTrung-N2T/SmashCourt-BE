namespace SmashCourt_BE.Data;

/// <summary>
/// Unit of Work interface for managing database context operations.
/// Currently provides change tracker management for handling concurrent booking conflicts.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Clears all tracked entities from the change tracker.
    /// Used after constraint violations to allow continued operations.
    /// </summary>
    void ClearChangeTracker();
}
