namespace SmashCourt_BE.Data;

/// <summary>
/// Unit of Work implementation that wraps SmashCourtContext operations.
/// Provides abstraction layer for context-specific operations to improve testability.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly SmashCourtContext _context;

    public UnitOfWork(SmashCourtContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Clears all tracked entities from the change tracker.
    /// This is typically called after a unique constraint violation to allow
    /// the service to continue processing (e.g., sending notifications) without
    /// the context being in a faulted state.
    /// </summary>
    public void ClearChangeTracker()
    {
        _context.ChangeTracker.Clear();
    }
}
