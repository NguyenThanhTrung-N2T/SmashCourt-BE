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
}
