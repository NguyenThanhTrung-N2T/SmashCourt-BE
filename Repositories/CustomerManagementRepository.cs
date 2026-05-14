using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories;

public class CustomerManagementRepository : ICustomerManagementRepository
{
    private readonly SmashCourtContext _context;

    public CustomerManagementRepository(SmashCourtContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách khách hàng với filter và phân trang
    /// AGGREGATE TẠI DB LEVEL - không load bookings vào memory
    /// </summary>
    public async Task<PagedResult<User>> GetCustomersAsync(CustomerListQuery query, Guid? managerBranchId)
    {
        // Base query - chỉ lấy User + CustomerLoyalty + Tier
        var customersQuery = _context.Users
            .Include(u => u.CustomerLoyalty)
                .ThenInclude(cl => cl!.Tier)
            .AsNoTracking()
            .Where(u => u.Role == UserRole.CUSTOMER); // Chỉ lấy CUSTOMER, không lẫn STAFF/MANAGER

        // BRANCH_MANAGER: Chỉ xem khách đã từng đặt sân tại chi nhánh mình
        if (managerBranchId.HasValue)
        {
            customersQuery = customersQuery.Where(u =>
                _context.Bookings.Any(b => b.CustomerId == u.Id && b.BranchId == managerBranchId.Value));
        }

        // Filter theo tìm kiếm
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchTerm = query.SearchTerm.Trim().ToLower();
            customersQuery = customersQuery.Where(u =>
                u.FullName.ToLower().Contains(searchTerm) ||
                u.Email.ToLower().Contains(searchTerm) ||
                (u.Phone != null && u.Phone.ToLower().Contains(searchTerm)));
        }

        // Filter theo hạng loyalty (ưu tiên TierId nếu có, fallback về Name)
        if (query.LoyaltyTierId.HasValue)
        {
            customersQuery = customersQuery.Where(u =>
                u.CustomerLoyalty != null && u.CustomerLoyalty.TierId == query.LoyaltyTierId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(query.LoyaltyTier))
        {
            customersQuery = customersQuery.Where(u =>
                u.CustomerLoyalty != null && u.CustomerLoyalty.Tier.Name == query.LoyaltyTier);
        }

        // Filter theo trạng thái
        if (query.Status.HasValue)
        {
            customersQuery = customersQuery.Where(u => u.Status == query.Status.Value);
        }

        // Filter theo chi nhánh (chỉ OWNER sử dụng)
        if (query.BranchId.HasValue && !managerBranchId.HasValue)
        {
            customersQuery = customersQuery.Where(u =>
                _context.Bookings.Any(b => b.CustomerId == u.Id && b.BranchId == query.BranchId.Value));
        }

        // Lấy tổng số record trước khi phân trang
        var totalItems = await customersQuery.CountAsync();

        // Sắp xếp
        customersQuery = query.SortBy.ToLower() switch
        {
            "fullname" => query.SortOrder.ToLower() == "asc"
                ? customersQuery.OrderBy(u => u.FullName)
                : customersQuery.OrderByDescending(u => u.FullName),
            _ => query.SortOrder.ToLower() == "asc"
                ? customersQuery.OrderBy(u => u.CreatedAt)
                : customersQuery.OrderByDescending(u => u.CreatedAt)
        };

        // Phân trang
        var customers = await customersQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return new PagedResult<User>
        {
            Items = customers,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    /// <summary>
    /// Lấy thông tin chi tiết khách hàng
    /// </summary>
    public async Task<User?> GetCustomerByIdAsync(Guid customerId)
    {
        return await _context.Users
            .Include(u => u.CustomerLoyalty)
                .ThenInclude(cl => cl!.Tier)
            .Include(u => u.OAuthAccounts)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == customerId && u.Role == UserRole.CUSTOMER);
    }

    /// <summary>
    /// Kiểm tra khách hàng có booking tại chi nhánh không
    /// </summary>
    public async Task<bool> HasBookingAtBranchAsync(Guid customerId, Guid branchId)
    {
        return await _context.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.CustomerId == customerId && b.BranchId == branchId);
    }

    /// <summary>
    /// Lấy lịch sử booking của khách hàng
    /// </summary>
    public async Task<PagedResult<Booking>> GetCustomerBookingsAsync(
        Guid customerId,
        CustomerBookingQuery query,
        Guid? managerBranchId)
    {
        var bookingsQuery = _context.Bookings
            .Include(b => b.Branch)
            .Include(b => b.BookingCourts)
                .ThenInclude(bc => bc.Court)
            .Include(b => b.Invoice)
            .AsNoTracking()
            .Where(b => b.CustomerId == customerId);

        // BRANCH_MANAGER: Chỉ xem booking tại chi nhánh mình
        if (managerBranchId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(b => b.BranchId == managerBranchId.Value);
        }

        // Filter theo chi nhánh (chỉ OWNER sử dụng)
        if (query.BranchId.HasValue && !managerBranchId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(b => b.BranchId == query.BranchId.Value);
        }

        // Filter theo trạng thái
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<BookingStatus>(query.Status, true, out var status))
            {
                bookingsQuery = bookingsQuery.Where(b => b.Status == status);
            }
        }

        // Filter theo thời gian
        if (query.FromDate.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(query.FromDate.Value);
            bookingsQuery = bookingsQuery.Where(b => b.BookingDate >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = DateOnly.FromDateTime(query.ToDate.Value);
            bookingsQuery = bookingsQuery.Where(b => b.BookingDate <= toDate);
        }

        // Lấy tổng số record
        var totalItems = await bookingsQuery.CountAsync();

        // Sắp xếp theo ngày đặt mới nhất
        var bookings = await bookingsQuery
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return new PagedResult<Booking>
        {
            Items = bookings,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    /// <summary>
    /// Lấy lịch sử tích điểm loyalty (chỉ OWNER)
    /// </summary>
    public async Task<PagedResult<LoyaltyTransaction>> GetLoyaltyTransactionsAsync(
        Guid customerId,
        LoyaltyTransactionQuery query)
    {
        var transactionsQuery = _context.LoyaltyTransactions
            .AsNoTracking()
            .Where(lt => lt.UserId == customerId);

        // Filter theo thời gian
        if (query.FromDate.HasValue)
        {
            transactionsQuery = transactionsQuery.Where(lt => lt.CreatedAt >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            transactionsQuery = transactionsQuery.Where(lt => lt.CreatedAt <= query.ToDate.Value);
        }

        // Lấy tổng số record
        var totalItems = await transactionsQuery.CountAsync();

        // Sắp xếp theo ngày mới nhất
        var transactions = await transactionsQuery
            .OrderByDescending(lt => lt.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return new PagedResult<LoyaltyTransaction>
        {
            Items = transactions,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    /// <summary>
    /// Lấy thống kê khách hàng
    /// </summary>
    public async Task<CustomerStatisticsDto> GetCustomerStatisticsAsync(Guid customerId, Guid? managerBranchId)
    {
        var bookingsQuery = _context.Bookings
            .AsNoTracking()
            .Where(b => b.CustomerId == customerId && b.Status == BookingStatus.COMPLETED);

        // BRANCH_MANAGER: Chỉ thống kê chi nhánh mình
        if (managerBranchId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(b => b.BranchId == managerBranchId.Value);
        }

        var statistics = new CustomerStatisticsDto
        {
            TotalCompletedBookings = await bookingsQuery.CountAsync(),
            LastBookingDate = await bookingsQuery
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.CreatedAt)
                .Select(b => (DateTime?)b.BookingDate.ToDateTime(TimeOnly.MinValue))
                .FirstOrDefaultAsync()
        };

        // Chỉ OWNER mới có thống kê chi tiết
        if (!managerBranchId.HasValue)
        {
            // Tổng doanh thu
            statistics.TotalRevenue = await _context.Invoices
                .AsNoTracking()
                .Where(i => i.Booking.CustomerId == customerId && i.Booking.Status == BookingStatus.COMPLETED)
                .SumAsync(i => i.FinalTotal);

            // Chi nhánh hay đặt nhất
            var mostBookedBranch = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.CustomerId == customerId && b.Status == BookingStatus.COMPLETED)
                .GroupBy(b => b.Branch.Name)
                .Select(g => new { BranchName = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            statistics.MostBookedBranch = mostBookedBranch?.BranchName;

            // Khung giờ hay đặt nhất (lấy từ BookingCourt)
            var mostBookedTimeSlot = await _context.BookingCourts
                .AsNoTracking()
                .Where(bc => bc.Booking.CustomerId == customerId && bc.Booking.Status == BookingStatus.COMPLETED)
                .GroupBy(bc => new { bc.StartTime, bc.EndTime })
                .Select(g => new
                {
                    StartTime = g.Key.StartTime,
                    EndTime = g.Key.EndTime,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            if (mostBookedTimeSlot != null)
            {
                statistics.MostBookedTimeSlot = $"{mostBookedTimeSlot.StartTime:HH\\:mm} - {mostBookedTimeSlot.EndTime:HH\\:mm}";
            }
        }

        return statistics;
    }

    /// <summary>
    /// Lấy số lượng booking COMPLETED của nhiều customers (batch query - tránh N+1)
    /// </summary>
    public async Task<Dictionary<Guid, int>> GetCompletedBookingCountBatchAsync(
        List<Guid> customerIds,
        Guid? managerBranchId)
    {
        var query = _context.Bookings
            .AsNoTracking()
            .Where(b => b.CustomerId.HasValue && 
                        customerIds.Contains(b.CustomerId.Value) && 
                        b.Status == BookingStatus.COMPLETED);

        // BRANCH_MANAGER: Chỉ đếm chi nhánh mình
        if (managerBranchId.HasValue)
        {
            query = query.Where(b => b.BranchId == managerBranchId.Value);
        }

        // GroupBy tại DB level - 1 query duy nhất
        var result = await query
            .GroupBy(b => b.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);

        return result;
    }
}
