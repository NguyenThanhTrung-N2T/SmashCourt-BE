using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

public class CustomerManagementService : ICustomerManagementService
{
    private readonly ICustomerManagementRepository _customerRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ILogger<CustomerManagementService> _logger;

    public CustomerManagementService(
        ICustomerManagementRepository customerRepo,
        IUserRepository userRepo,
        IUserBranchRepository userBranchRepo,
        IRefreshTokenRepository refreshTokenRepo,
        ILogger<CustomerManagementService> logger)
    {
        _customerRepo = customerRepo;
        _userRepo = userRepo;
        _userBranchRepo = userBranchRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _logger = logger;
    }

    #region Helper Methods

    /// <summary>
    /// Lấy BranchId của BRANCH_MANAGER (null nếu OWNER)
    /// </summary>
    private async Task<Guid?> GetManagerBranchIdAsync(Guid currentUserId, string currentUserRole)
    {
        if (currentUserRole == UserRole.OWNER.ToString())
            return null;

        if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
            if (managerBranch == null)
                throw new AppException(403, "Bạn chưa được gán chi nhánh", ErrorCodes.Forbidden);

            return managerBranch.BranchId;
        }

        throw new AppException(403, "Bạn không có quyền truy cập module này", ErrorCodes.Forbidden);
    }

    /// <summary>
    /// Validate quyền truy cập customer
    /// OWNER: Có thể truy cập tất cả customers
    /// BRANCH_MANAGER: Chỉ có thể truy cập customer đã từng đặt sân tại chi nhánh mình
    /// </summary>
    private async Task ValidateAccessToCustomerAsync(Guid customerId, Guid? managerBranchId)
    {
        // OWNER có full access
        if (!managerBranchId.HasValue)
            return;

        // BRANCH_MANAGER: Check customer có booking tại chi nhánh không
        var hasBooking = await _customerRepo.HasBookingAtBranchAsync(customerId, managerBranchId.Value);
        if (!hasBooking)
            throw new AppException(403, "Bạn chỉ có thể xem khách hàng đã từng đặt sân tại chi nhánh của mình", ErrorCodes.Forbidden);
    }

    /// <summary>
    /// Map User entity sang CustomerListDto
    /// TotalCompletedBookings sẽ được lấy riêng từ DB (không đếm trong memory)
    /// </summary>
    private CustomerListDto MapToCustomerListDto(User customer, bool isOwner, int completedBookingCount)
    {
        var dto = new CustomerListDto
        {
            Id = customer.Id,
            FullName = customer.FullName,
            Phone = customer.Phone,
            LoyaltyTier = customer.CustomerLoyalty?.Tier.Name ?? "Bronze",
            TotalCompletedBookings = completedBookingCount,
            Status = customer.Status.ToString(),
            CreatedAt = customer.CreatedAt
        };

        // Chỉ OWNER mới thấy email và totalPoints
        if (isOwner)
        {
            dto.Email = customer.Email;
            dto.TotalPoints = customer.CustomerLoyalty?.TotalPoints ?? 0;
        }

        return dto;
    }

    /// <summary>
    /// Map User entity sang CustomerDetailDto
    /// </summary>
    private async Task<CustomerDetailDto> MapToCustomerDetailDtoAsync(User customer, bool isOwner, Guid? managerBranchId)
    {
        var loyaltyTier = customer.CustomerLoyalty?.Tier;
        var currentDiscount = loyaltyTier?.DiscountRate ?? 0;

        var dto = new CustomerDetailDto
        {
            Id = customer.Id,
            FullName = customer.FullName,
            Phone = customer.Phone,
            AvatarUrl = customer.AvatarUrl,
            LoyaltyTier = loyaltyTier?.Name ?? "Bronze",
            CurrentDiscount = currentDiscount,
            Status = customer.Status.ToString(),
            CreatedAt = customer.CreatedAt,
            Statistics = await _customerRepo.GetCustomerStatisticsAsync(customer.Id, managerBranchId)
        };

        // Chỉ OWNER mới thấy thông tin đầy đủ
        if (isOwner)
        {
            dto.Email = customer.Email;
            dto.TotalPoints = customer.CustomerLoyalty?.TotalPoints ?? 0;

            // Tính điểm cần thêm để lên hạng tiếp theo
            // TODO: Cần lấy danh sách tiers từ DB để tính chính xác
            dto.PointsToNextTier = 0; // Placeholder

            // Phương thức đăng ký
            var hasOAuth = customer.OAuthAccounts.Any();
            dto.RegistrationMethod = hasOAuth
                ? customer.OAuthAccounts.First().Provider
                : "Email";
        }

        // Thông tin khóa tài khoản
        if (customer.Status == UserStatus.LOCKED && customer.LockedBy.HasValue)
        {
            var lockedByUser = await _userRepo.GetUserByIdAsync(customer.LockedBy.Value);
            dto.LockInfo = new LockInfoDto
            {
                Reason = customer.LockReason,
                LockedAt = customer.LockedAt,
                LockedBy = customer.LockedBy,
                LockedByName = lockedByUser?.FullName
            };
        }

        return dto;
    }

    /// <summary>
    /// Map Booking entity sang CustomerBookingDto
    /// </summary>
    private CustomerBookingDto MapToCustomerBookingDto(Booking booking)
    {
        // Lấy thời gian bắt đầu và kết thúc từ BookingCourts
        var bookingCourts = booking.BookingCourts.OrderBy(bc => bc.StartTime).ToList();
        var startTime = bookingCourts.FirstOrDefault()?.StartTime ?? TimeOnly.MinValue;
        var endTime = bookingCourts.LastOrDefault()?.EndTime ?? TimeOnly.MinValue;

        // Lấy danh sách tên sân
        var courtNames = string.Join(", ", bookingCourts.Select(bc => bc.Court.Name).Distinct());

        return new CustomerBookingDto
        {
            BookingId = booking.Id,
            BranchName = booking.Branch.Name,
            BookingDate = booking.BookingDate,
            CourtNames = courtNames,
            StartTime = startTime,
            EndTime = endTime,
            TotalAmount = booking.Invoice?.FinalTotal ?? 0,
            Status = booking.Status.ToString(),
            CreatedAt = booking.CreatedAt
        };
    }

    /// <summary>
    /// Map LoyaltyTransaction entity sang LoyaltyTransactionDto
    /// </summary>
    private LoyaltyTransactionDto MapToLoyaltyTransactionDto(LoyaltyTransaction transaction)
    {
        return new LoyaltyTransactionDto
        {
            TransactionId = transaction.Id,
            BookingId = transaction.BookingId,
            Points = transaction.Points,
            TotalPointsAfter = transaction.TotalPointsAfter,
            Type = transaction.Type.ToString(),
            Note = transaction.Note,
            CreatedAt = transaction.CreatedAt
        };
    }

    /// <summary>
    /// Invalidate tất cả refresh tokens của customer (dùng khi lock)
    /// </summary>
    private async Task InvalidateAllCustomerTokensAsync(Guid customerId)
    {
        try
        {
            await _refreshTokenRepo.RevokeAllByUserIdAsync(customerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke tokens for customer {CustomerId}", customerId);
            // Không throw exception - tokens sẽ tự hết hạn
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Lấy danh sách khách hàng với filter và phân trang
    /// Tối ưu: Dùng batch query để tránh N+1 problem
    /// </summary>
    public async Task<PagedResult<CustomerListDto>> GetCustomersAsync(
        CustomerListQuery query,
        Guid currentUserId,
        string currentUserRole)
    {
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var isOwner = currentUserRole == UserRole.OWNER.ToString();

        var result = await _customerRepo.GetCustomersAsync(query, managerBranchId);

        // Lấy booking count cho TẤT CẢ customers trong 1 query duy nhất (tránh N+1)
        var customerIds = result.Items.Select(c => c.Id).ToList();
        var bookingCounts = await _customerRepo.GetCompletedBookingCountBatchAsync(customerIds, managerBranchId);

        // Map với booking count từ dictionary
        var customerDtos = result.Items.Select(customer =>
        {
            var bookingCount = bookingCounts.GetValueOrDefault(customer.Id, 0);
            return MapToCustomerListDto(customer, isOwner, bookingCount);
        }).ToList();

        return new PagedResult<CustomerListDto>
        {
            Items = customerDtos,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems
        };
    }

    /// <summary>
    /// Lấy thông tin chi tiết khách hàng
    /// </summary>
    public async Task<CustomerDetailDto> GetCustomerByIdAsync(
        Guid customerId,
        Guid currentUserId,
        string currentUserRole)
    {
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var isOwner = currentUserRole == UserRole.OWNER.ToString();

        // Validate quyền truy cập
        await ValidateAccessToCustomerAsync(customerId, managerBranchId);

        // Lấy thông tin customer
        var customer = await _customerRepo.GetCustomerByIdAsync(customerId);
        if (customer == null)
            throw new AppException(404, "Không tìm thấy khách hàng", ErrorCodes.UserNotFound);

        return await MapToCustomerDetailDtoAsync(customer, isOwner, managerBranchId);
    }

    /// <summary>
    /// Lấy lịch sử booking của khách hàng
    /// </summary>
    public async Task<PagedResult<CustomerBookingDto>> GetCustomerBookingsAsync(
        Guid customerId,
        CustomerBookingQuery query,
        Guid currentUserId,
        string currentUserRole)
    {
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);

        // Validate quyền truy cập
        await ValidateAccessToCustomerAsync(customerId, managerBranchId);

        var result = await _customerRepo.GetCustomerBookingsAsync(customerId, query, managerBranchId);

        return new PagedResult<CustomerBookingDto>
        {
            Items = result.Items.Select(MapToCustomerBookingDto).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems
        };
    }

    /// <summary>
    /// Lấy lịch sử tích điểm loyalty (chỉ OWNER)
    /// </summary>
    public async Task<PagedResult<LoyaltyTransactionDto>> GetLoyaltyTransactionsAsync(
        Guid customerId,
        LoyaltyTransactionQuery query,
        Guid currentUserId,
        string currentUserRole)
    {
        // Chỉ OWNER mới có quyền xem loyalty transactions
        if (currentUserRole != UserRole.OWNER.ToString())
            throw new AppException(403, "Chỉ OWNER mới có quyền xem lịch sử tích điểm", ErrorCodes.Forbidden);

        // Kiểm tra customer tồn tại
        var customer = await _customerRepo.GetCustomerByIdAsync(customerId);
        if (customer == null)
            throw new AppException(404, "Không tìm thấy khách hàng", ErrorCodes.UserNotFound);

        var result = await _customerRepo.GetLoyaltyTransactionsAsync(customerId, query);

        return new PagedResult<LoyaltyTransactionDto>
        {
            Items = result.Items.Select(MapToLoyaltyTransactionDto).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems
        };
    }

    /// <summary>
    /// Lấy thống kê khách hàng
    /// </summary>
    public async Task<CustomerStatisticsDto> GetCustomerStatisticsAsync(
        Guid customerId,
        Guid currentUserId,
        string currentUserRole)
    {
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);

        // Validate quyền truy cập
        await ValidateAccessToCustomerAsync(customerId, managerBranchId);

        return await _customerRepo.GetCustomerStatisticsAsync(customerId, managerBranchId);
    }

    /// <summary>
    /// Khóa tài khoản khách hàng (chỉ OWNER)
    /// </summary>
    public async Task LockCustomerAsync(
        Guid customerId,
        LockCustomerDto dto,
        Guid currentUserId,
        string currentUserRole)
    {
        // Chỉ OWNER mới có quyền lock customer
        if (currentUserRole != UserRole.OWNER.ToString())
            throw new AppException(403, "Chỉ OWNER mới có quyền khóa tài khoản khách hàng", ErrorCodes.Forbidden);

        // Lấy thông tin customer
        var customer = await _userRepo.GetUserByIdAsync(customerId);
        if (customer == null)
            throw new AppException(404, "Không tìm thấy khách hàng", ErrorCodes.UserNotFound);

        // Kiểm tra có phải CUSTOMER không
        if (customer.Role != UserRole.CUSTOMER)
            throw new AppException(400, "Chỉ có thể khóa tài khoản khách hàng", ErrorCodes.BadRequest);

        // Kiểm tra đã bị khóa chưa
        if (customer.Status == UserStatus.LOCKED)
            throw new AppException(400, "Tài khoản khách hàng đã bị khóa", ErrorCodes.UserAlreadyLocked);

        // Khóa customer
        customer.Status = UserStatus.LOCKED;
        customer.LockReason = dto.Reason;
        customer.LockedAt = DateTime.UtcNow;
        customer.LockedBy = currentUserId;
        customer.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(customer);

        // Invalidate tất cả refresh tokens
        await InvalidateAllCustomerTokensAsync(customerId);

        _logger.LogInformation(
            "Customer {CustomerId} locked by {UserId}. Reason: {Reason}",
            customerId, currentUserId, dto.Reason);
    }

    /// <summary>
    /// Mở khóa tài khoản khách hàng (chỉ OWNER)
    /// </summary>
    public async Task UnlockCustomerAsync(
        Guid customerId,
        Guid currentUserId,
        string currentUserRole)
    {
        // Chỉ OWNER mới có quyền unlock customer
        if (currentUserRole != UserRole.OWNER.ToString())
            throw new AppException(403, "Chỉ OWNER mới có quyền mở khóa tài khoản khách hàng", ErrorCodes.Forbidden);

        // Lấy thông tin customer
        var customer = await _userRepo.GetUserByIdAsync(customerId);
        if (customer == null)
            throw new AppException(404, "Không tìm thấy khách hàng", ErrorCodes.UserNotFound);

        // Kiểm tra có phải CUSTOMER không
        if (customer.Role != UserRole.CUSTOMER)
            throw new AppException(400, "Chỉ có thể mở khóa tài khoản khách hàng", ErrorCodes.BadRequest);

        // Kiểm tra có bị khóa không
        if (customer.Status != UserStatus.LOCKED)
            throw new AppException(400, "Tài khoản khách hàng không bị khóa", ErrorCodes.UserNotLocked);

        // Mở khóa
        customer.Status = UserStatus.ACTIVE;
        customer.LockReason = null;
        customer.LockedAt = null;
        customer.LockedBy = null;
        customer.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(customer);

        _logger.LogInformation(
            "Customer {CustomerId} unlocked by {UserId}",
            customerId, currentUserId);
    }

    #endregion
}
