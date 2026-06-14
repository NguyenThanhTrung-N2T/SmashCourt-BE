using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmashCourt_BE.Repositories.IRepository;
using System.Security.Claims;

namespace SmashCourt_BE.Hubs;

/// <summary>
/// Hub quản lý kết nối real-time notifications
/// Client tự động được phân nhóm theo UserId, UserRole và BranchId
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IUserBranchRepository userBranchRepo,
        ILogger<NotificationHub> logger)
    {
        _userBranchRepo = userBranchRepo;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userRole))
        {
            _logger.LogWarning(
                "SignalR connection rejected: Missing UserId or UserRole. ConnectionId={ConnectionId}",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        // Group theo cá nhân: user_{UserId}
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

        // Group theo quyền: role_{UserRole}
        await Groups.AddToGroupAsync(Context.ConnectionId, $"role_{userRole}");

        // Group theo chi nhánh (chỉ cho BRANCH_MANAGER và STAFF)
        if (userRole == "BRANCH_MANAGER" || userRole == "STAFF")
        {
            if (Guid.TryParse(userId, out var userGuid))
            {
                var userBranch = await _userBranchRepo.GetActiveByUserIdAsync(userGuid);
                if (userBranch != null)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"branch_{userBranch.BranchId}");
                    
                    _logger.LogInformation(
                        "SignalR Connected: UserId={UserId}, Role={Role}, BranchId={BranchId}, ConnectionId={ConnectionId}",
                        userId, userRole, userBranch.BranchId, Context.ConnectionId);
                }
                else
                {
                    _logger.LogWarning(
                        "SignalR Connected but user has no active branch: UserId={UserId}, Role={Role}, ConnectionId={ConnectionId}",
                        userId, userRole, Context.ConnectionId);
                }
            }
        }
        else
        {
            _logger.LogInformation(
                "SignalR Connected: UserId={UserId}, Role={Role}, ConnectionId={ConnectionId}",
                userId, userRole, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (exception != null)
        {
            _logger.LogWarning(exception,
                "SignalR Disconnected with error: UserId={UserId}, Role={Role}, ConnectionId={ConnectionId}",
                userId, userRole, Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation(
                "SignalR Disconnected: UserId={UserId}, Role={Role}, ConnectionId={ConnectionId}",
                userId, userRole, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Public TimeGrid Channels (customers viewing a specific date/court-type) ──

    /// <summary>
    /// Subscribes the current connection to the timegrid channel for a specific
    /// branch + court type + date combination.
    /// Group key: timegrid_{branchId}_{courtTypeId}_{date:yyyy-MM-dd}
    /// Call this when the customer navigates to the timegrid page.
    /// </summary>
    public async Task JoinTimeGrid(Guid branchId, Guid courtTypeId, DateOnly date)
    {
        var group = TimeGridGroup(branchId, courtTypeId, date);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        _logger.LogDebug(
            "SignalR JoinTimeGrid: ConnectionId={ConnectionId}, Group={Group}",
            Context.ConnectionId, group);
    }

    /// <summary>
    /// Removes the current connection from the timegrid channel.
    /// Call this when the customer leaves the timegrid page or changes date/court-type.
    /// </summary>
    public async Task LeaveTimeGrid(Guid branchId, Guid courtTypeId, DateOnly date)
    {
        var group = TimeGridGroup(branchId, courtTypeId, date);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

        _logger.LogDebug(
            "SignalR LeaveTimeGrid: ConnectionId={ConnectionId}, Group={Group}",
            Context.ConnectionId, group);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static string TimeGridGroup(Guid branchId, Guid courtTypeId, DateOnly date)
        => $"timegrid_{branchId}_{courtTypeId}_{date:yyyy-MM-dd}";
}

