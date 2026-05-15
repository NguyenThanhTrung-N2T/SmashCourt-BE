using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories;

public class ReportRepository : IReportRepository
{
    private readonly SmashCourtContext _context;

    public ReportRepository(SmashCourtContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy tổng quan metrics cho dashboard
    /// </summary>
    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        // Base query cho bookings trong khoảng thời gian
        var bookingsQuery = _context.Bookings
            .AsNoTracking()
            .Where(b => b.BookingDate >= fromDate && b.BookingDate <= toDate);

        // Filter theo branch nếu có
        if (branchId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(b => b.BranchId == branchId.Value);
        }

        // Tính tổng doanh thu từ COMPLETED bookings
        var totalRevenue = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .SumAsync(i => i.FinalTotal);

        // Đếm bookings theo status
        var bookingStats = await bookingsQuery
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalBookings = bookingStats.Sum(s => s.Count);
        var completedBookings = bookingStats.FirstOrDefault(s => s.Status == BookingStatus.COMPLETED)?.Count ?? 0;
        var cancelledBookings = bookingStats.FirstOrDefault(s => s.Status == BookingStatus.CANCELLED)?.Count ?? 0;
        var noShowBookings = bookingStats.FirstOrDefault(s => s.Status == BookingStatus.NO_SHOW)?.Count ?? 0;

        // Đếm khách hàng mới:
        // - Không filter branch: customer đăng ký trong range
        // - Filter branch: customer có booking ĐẦU TIÊN tại branch nằm trong range
        int newCustomers;
        if (branchId.HasValue)
        {
            newCustomers = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.BranchId == branchId.Value && b.CustomerId.HasValue)
                .GroupBy(b => b.CustomerId!.Value)
                .Select(g => g.Min(b => b.BookingDate))
                .Where(firstBooking => firstBooking >= fromDate && firstBooking <= toDate)
                .CountAsync();
        }
        else
        {
            // Fix: PostgreSQL yêu cầu DateTime phải có Kind = UTC cho timestamp with time zone
            var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            
            newCustomers = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.CUSTOMER &&
                            u.CreatedAt >= fromDateTime &&
                            u.CreatedAt <= toDateTime)
                .CountAsync();
        }

        // Tính occupancy rate
        var occupancyRate = await CalculateOccupancyRateAsync(fromDate, toDate, branchId);

        // Doanh thu theo payment method
        // Lấy từ Invoice + Payment để tránh double-count
        var invoicesWithPayment = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .Select(i => new
            {
                i.FinalTotal,
                PaymentMethod = i.Payments
                    .Where(p => p.Status == PaymentTxStatus.SUCCESS)
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => p.Method)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var onlineRevenue = invoicesWithPayment
            .Where(i => i.PaymentMethod == PaymentTxMethod.VNPAY)
            .Sum(i => i.FinalTotal);
        var cashRevenue = invoicesWithPayment
            .Where(i => i.PaymentMethod == PaymentTxMethod.CASH)
            .Sum(i => i.FinalTotal);

        return new DashboardSummaryDto
        {
            TotalRevenue = totalRevenue,
            TotalBookings = totalBookings,
            CompletedBookings = completedBookings,
            CancelledBookings = cancelledBookings,
            NoShowBookings = noShowBookings,
            NewCustomers = newCustomers,
            OccupancyRate = occupancyRate,
            OnlinePaymentRevenue = onlineRevenue,
            CashPaymentRevenue = cashRevenue
        };
    }

    /// <summary>
    /// Lấy top 5 chi nhánh theo doanh thu (chỉ OWNER)
    /// </summary>
    public async Task<List<TopBranchDto>> GetTopBranchesAsync(
        DateOnly fromDate, DateOnly toDate, int limit)
    {
        return await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate)
            .GroupBy(i => new { i.Booking.BranchId, i.Booking.Branch.Name })
            .Select(g => new TopBranchDto
            {
                BranchId = g.Key.BranchId,
                BranchName = g.Key.Name,
                Revenue = g.Sum(i => i.FinalTotal),
                BookingCount = g.Count()
            })
            .OrderByDescending(b => b.Revenue)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Lấy top 5 khách hàng theo doanh thu
    /// </summary>
    public async Task<List<TopCustomerDto>> GetTopCustomersAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, int limit)
    {
        var query = _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        i.Booking.CustomerId.HasValue &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value));

        return await query
            .GroupBy(i => new
            {
                CustomerId = i.Booking.CustomerId!.Value,
                i.Booking.Customer!.FullName,
                LoyaltyTier = i.Booking.Customer.CustomerLoyalty != null
                    ? i.Booking.Customer.CustomerLoyalty.Tier.Name
                    : "Bronze"
            })
            .Select(g => new TopCustomerDto
            {
                CustomerId = g.Key.CustomerId,
                FullName = g.Key.FullName,
                TotalRevenue = g.Sum(i => i.FinalTotal),
                BookingCount = g.Count(),
                LoyaltyTier = g.Key.LoyaltyTier
            })
            .OrderByDescending(c => c.TotalRevenue)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Lấy xu hướng doanh thu theo ngày
    /// </summary>
    public async Task<List<RevenueTrendDto>> GetRevenueTrendAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        // Query data grouped by BookingDate
        var data = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .GroupBy(i => i.Booking.BookingDate)
            .Select(g => new
            {
                Date = g.Key,
                Revenue = g.Sum(i => i.FinalTotal),
                BookingCount = g.Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        // Format Period in memory (DateOnly.ToString cannot be translated to SQL)
        return data.Select(d => new RevenueTrendDto
        {
            Period = d.Date.ToString("yyyy-MM-dd"),
            Revenue = d.Revenue,
            BookingCount = d.BookingCount
        }).ToList();
    }

    /// <summary>
    /// Lấy xu hướng booking theo ngày
    /// </summary>
    public async Task<List<BookingTrendDto>> GetBookingTrendAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        // Query data grouped by BookingDate
        var data = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.BookingDate >= fromDate &&
                        b.BookingDate <= toDate &&
                        (!branchId.HasValue || b.BranchId == branchId.Value))
            .GroupBy(b => b.BookingDate)
            .Select(g => new
            {
                Date = g.Key,
                TotalCount = g.Count(),
                CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED)
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        // Format Period in memory (DateOnly.ToString cannot be translated to SQL)
        return data.Select(d => new BookingTrendDto
        {
            Period = d.Date.ToString("yyyy-MM-dd"),
            TotalCount = d.TotalCount,
            CompletedCount = d.CompletedCount
        }).ToList();
    }

    /// <summary>
    /// Lấy báo cáo doanh thu với grouping
    /// </summary>
    public async Task<RevenueReportDto> GetRevenueReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        var invoicesQuery = _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value));

        // Gom tất cả aggregate metrics trong 1 query duy nhất
        var metrics = await invoicesQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRevenue    = g.Sum(i => i.FinalTotal),
                CourtRevenue    = g.Sum(i => i.CourtFee),
                ServiceRevenue  = g.Sum(i => i.ServiceFee),
                DiscountAmount  = g.Sum(i => i.LoyaltyDiscountAmount + i.PromotionDiscountAmount),
                BookingCount    = g.Count()
            })
            .FirstOrDefaultAsync();

        var totalRevenue         = metrics?.TotalRevenue ?? 0;
        var courtRevenue         = metrics?.CourtRevenue ?? 0;
        var serviceRevenue       = metrics?.ServiceRevenue ?? 0;
        var discountAmount        = metrics?.DiscountAmount ?? 0;
        var bookingCount         = metrics?.BookingCount ?? 0;
        var averageBookingValue  = bookingCount > 0 ? totalRevenue / bookingCount : 0;

        // Group items theo groupBy parameter
        var items = await GetRevenueItemsAsync(invoicesQuery, groupBy);

        return new RevenueReportDto
        {
            TotalRevenue        = totalRevenue,
            CourtRevenue        = courtRevenue,
            ServiceRevenue      = serviceRevenue,
            DiscountAmount      = discountAmount,
            AverageBookingValue = averageBookingValue,
            Items               = items
        };
    }

    /// <summary>
    /// Lấy báo cáo booking với grouping
    /// </summary>
    public async Task<BookingReportDto> GetBookingReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        var bookingsQuery = _context.Bookings
            .AsNoTracking()
            .Where(b => b.BookingDate >= fromDate &&
                        b.BookingDate <= toDate &&
                        (!branchId.HasValue || b.BranchId == branchId.Value));

        // Đếm theo status
        var statusCounts = await bookingsQuery
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalBookings = statusCounts.Sum(s => s.Count);
        var completed = statusCounts.FirstOrDefault(s => s.Status == BookingStatus.COMPLETED)?.Count ?? 0;
        var cancelled = statusCounts.FirstOrDefault(s => s.Status == BookingStatus.CANCELLED)?.Count ?? 0;
        var noShow = statusCounts.FirstOrDefault(s => s.Status == BookingStatus.NO_SHOW)?.Count ?? 0;
        var pendingPayment = statusCounts.FirstOrDefault(s => s.Status == BookingStatus.PENDING_PAYMENT)?.Count ?? 0;

        // Đếm theo source
        var sourceCounts = await bookingsQuery
            .GroupBy(b => b.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync();

        var onlineBookings = sourceCounts.FirstOrDefault(s => s.Source == BookingSource.ONLINE)?.Count ?? 0;
        var walkInBookings = sourceCounts.FirstOrDefault(s => s.Source == BookingSource.WALK_IN)?.Count ?? 0;

        // Tính rates
        var cancellationRate = totalBookings > 0 ? (decimal)cancelled / totalBookings * 100 : 0;
        var noShowRate = totalBookings > 0 ? (decimal)noShow / totalBookings * 100 : 0;

        // Group items
        var items = await GetBookingItemsAsync(bookingsQuery, groupBy);

        return new BookingReportDto
        {
            TotalBookings = totalBookings,
            Completed = completed,
            Cancelled = cancelled,
            NoShow = noShow,
            PendingPayment = pendingPayment,
            OnlineBookings = onlineBookings,
            WalkInBookings = walkInBookings,
            CancellationRate = Math.Round(cancellationRate, 1),
            NoShowRate = Math.Round(noShowRate, 1),
            Items = items
        };
    }

    #region Private Helper Methods

    /// <summary>
    /// Tính occupancy rate
    /// </summary>
    private async Task<decimal> CalculateOccupancyRateAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        var branchesQuery = _context.Branches.AsNoTracking();
        if (branchId.HasValue)
            branchesQuery = branchesQuery.Where(b => b.Id == branchId.Value);

        var branches = await branchesQuery.ToListAsync();
        if (!branches.Any()) return 0;

        // Batch query court counts trong 1 query (fix N+1)
        var branchIds = branches.Select(b => b.Id).ToList();
        var courtCounts = await _context.Courts
            .AsNoTracking()
            .Where(c => branchIds.Contains(c.BranchId) && c.Status != CourtStatus.SUSPENDED)
            .GroupBy(c => c.BranchId)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BranchId, x => x.Count);

        var days = (toDate.ToDateTime(TimeOnly.MinValue) - fromDate.ToDateTime(TimeOnly.MinValue)).Days + 1;
        decimal totalAvailableHours = 0;

        foreach (var branch in branches)
        {
            var courtCount = courtCounts.GetValueOrDefault(branch.Id, 0);
            var operatingHours = (branch.CloseTime - branch.OpenTime).TotalHours;
            totalAvailableHours += (decimal)(courtCount * operatingHours * days);
        }

        if (totalAvailableHours == 0) return 0;

        var bookedHours = await _context.BookingCourts
            .AsNoTracking()
            .Where(bc => (bc.Booking.Status == BookingStatus.COMPLETED ||
                          bc.Booking.Status == BookingStatus.IN_PROGRESS) &&
                         bc.Booking.BookingDate >= fromDate &&
                         bc.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bc.Booking.BranchId == branchId.Value))
            .SumAsync(bc => (decimal)(bc.EndTime - bc.StartTime).TotalHours);

        return Math.Round(bookedHours / totalAvailableHours * 100, 1);
    }

    /// <summary>
    /// Group revenue items theo groupBy parameter
    /// </summary>
    private async Task<List<RevenueItemDto>> GetRevenueItemsAsync(
        IQueryable<Models.Entities.Invoice> query, string? groupBy)
    {
        var normalizedGroupBy = string.IsNullOrEmpty(groupBy) ? "day" : groupBy.ToLower();

        if (normalizedGroupBy == "day")
        {
            // Query data grouped by BookingDate
            var data = await query
                .GroupBy(i => i.Booking.BookingDate)
                .Select(g => new
                {
                    Date = g.Key,
                    Revenue = g.Sum(i => i.FinalTotal),
                    BookingCount = g.Count()
                })
                .OrderBy(r => r.Date)
                .ToListAsync();

            // Format Period in memory (DateOnly.ToString cannot be translated to SQL)
            return data.Select(d => new RevenueItemDto
            {
                Period = d.Date.ToString("yyyy-MM-dd"),
                Revenue = d.Revenue,
                BookingCount = d.BookingCount
            }).ToList();
        }

        var validValues = new[] { "day", "week", "month", "branch", "courttype", "paymentmethod", "hour", "dayofweek" };
        if (!validValues.Contains(normalizedGroupBy))
            throw new AppException(400,
                $"groupBy '{groupBy}' không hợp lệ. Các giá trị hợp lệ: day, week, month, branch, courtType, paymentMethod, hour, dayOfWeek",
                ErrorCodes.BadRequest);

        throw new AppException(400,
            $"groupBy '{groupBy}' chưa được hỗ trợ. Hiện tại chỉ hỗ trợ: day",
            ErrorCodes.BadRequest);
    }

    /// <summary>
    /// Group booking items theo groupBy parameter
    /// </summary>
    private async Task<List<BookingItemDto>> GetBookingItemsAsync(
        IQueryable<Models.Entities.Booking> query, string? groupBy)
    {
        var normalizedGroupBy = string.IsNullOrEmpty(groupBy) ? "day" : groupBy.ToLower();

        if (normalizedGroupBy == "day")
        {
            // Query data grouped by BookingDate
            var data = await query
                .GroupBy(b => b.BookingDate)
                .Select(g => new
                {
                    Date = g.Key,
                    BookingCount = g.Count(),
                    CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED),
                    CancelledCount = g.Count(b => b.Status == BookingStatus.CANCELLED)
                })
                .OrderBy(b => b.Date)
                .ToListAsync();

            // Format Period in memory (DateOnly.ToString cannot be translated to SQL)
            return data.Select(d => new BookingItemDto
            {
                Period = d.Date.ToString("yyyy-MM-dd"),
                BookingCount = d.BookingCount,
                CompletedCount = d.CompletedCount,
                CancelledCount = d.CancelledCount
            }).ToList();
        }

        var validValues = new[] { "day", "week", "month", "branch", "courttype", "paymentmethod", "hour", "dayofweek" };
        if (!validValues.Contains(normalizedGroupBy))
            throw new AppException(400,
                $"groupBy '{groupBy}' không hợp lệ. Các giá trị hợp lệ: day, week, month, branch, courtType, paymentMethod, hour, dayOfWeek",
                ErrorCodes.BadRequest);

        throw new AppException(400,
            $"groupBy '{groupBy}' chưa được hỗ trợ. Hiện tại chỉ hỗ trợ: day",
            ErrorCodes.BadRequest);
    }

    #endregion
}
