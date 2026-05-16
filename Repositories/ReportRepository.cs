using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories;

public class ReportRepository : IReportRepository
{
    private readonly SmashCourtContext _context;

    // Constants cho giới hạn kết quả trả về
    private const int TOP_BRANCHES_LIMIT = 5;
    private const int TOP_CUSTOMERS_LIMIT = 5;
    private const int TOP_COURTS_LIMIT = 10;
    private const int TOP_SERVICES_LIMIT = 10;
    private const int TOP_PROMOTIONS_LIMIT = 10;
    private const int PEAK_HOURS_COUNT = 3;
    private const int OFF_PEAK_HOURS_COUNT = 3;
    private const int MAX_COURT_UTILIZATION_ITEMS = 100;

    public ReportRepository(SmashCourtContext context)
    {
        _context = context;
    }

    #region Dashboard & Overview Reports

    /// <summary>
    /// Lấy tổng quan metrics cho dashboard theo khoảng thời gian
    /// </summary>
    /// <param name="fromDate">Ngày bắt đầu của khoảng thời gian báo cáo</param>
    /// <param name="toDate">Ngày kết thúc của khoảng thời gian báo cáo</param>
    /// <param name="branchId">ID chi nhánh để filter (nullable, chỉ OWNER có thể filter)</param>
    /// <returns>DTO chứa các metrics tổng quan: doanh thu, số booking, khách hàng mới, occupancy rate</returns>
    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        // Tạo base query cho bookings trong khoảng thời gian
        var bookingsQuery = _context.Bookings
            .AsNoTracking()
            .Where(b => b.BookingDate >= fromDate && b.BookingDate <= toDate);

        if (branchId.HasValue)
        {
            bookingsQuery = bookingsQuery.Where(b => b.BranchId == branchId.Value);
        }

        // Chỉ tính doanh thu từ booking COMPLETED, loại trừ CANCELLED/NO_SHOW theo yêu cầu nghiệp vụ
        var totalRevenue = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .SumAsync(i => i.FinalTotal);

        // Đếm số lượng bookings theo từng trạng thái
        var bookingStatusCounts = await bookingsQuery
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalBookings = bookingStatusCounts.Sum(s => s.Count);
        var completedBookings = bookingStatusCounts.FirstOrDefault(s => s.Status == BookingStatus.COMPLETED)?.Count ?? 0;
        var cancelledBookings = bookingStatusCounts.FirstOrDefault(s => s.Status == BookingStatus.CANCELLED)?.Count ?? 0;
        var noShowBookings = bookingStatusCounts.FirstOrDefault(s => s.Status == BookingStatus.NO_SHOW)?.Count ?? 0;

        // Logic đếm khách hàng mới:
        // - Không có branchId: đếm customer đăng ký (CreatedAt) trong khoảng thời gian
        // - Có branchId: đếm customer có booking ĐẦU TIÊN tại branch đó trong khoảng thời gian
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
            // PostgreSQL yêu cầu DateTime phải có Kind = UTC cho timestamp with time zone
            var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            newCustomers = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.CUSTOMER &&
                            u.CreatedAt >= fromDateTime &&
                            u.CreatedAt <= toDateTime)
                .CountAsync();
        }

        var occupancyRate = await CalculateOccupancyRateAsync(fromDate, toDate, branchId);

        // Phân loại doanh thu theo phương thức thanh toán (VNPAY online, CASH tại quầy)
        // Lấy payment method từ Payment record thành công gần nhất để tránh đếm trùng
        var invoicesWithPaymentMethod = await _context.Invoices
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

        var onlineRevenue = invoicesWithPaymentMethod
            .Where(i => i.PaymentMethod == PaymentTxMethod.VNPAY)
            .Sum(i => i.FinalTotal);
        var cashRevenue = invoicesWithPaymentMethod
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
    /// Lấy danh sách top chi nhánh có doanh thu cao nhất (chỉ dành cho OWNER)
    /// </summary>
    /// <param name="fromDate">Ngày bắt đầu của khoảng thời gian báo cáo</param>
    /// <param name="toDate">Ngày kết thúc của khoảng thời gian báo cáo</param>
    /// <param name="limit">Số lượng chi nhánh tối đa trả về</param>
    /// <returns>Danh sách chi nhánh được sắp xếp theo doanh thu giảm dần</returns>
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
    /// Lấy danh sách top khách hàng có doanh thu cao nhất
    /// </summary>
    /// <param name="fromDate">Ngày bắt đầu của khoảng thời gian báo cáo</param>
    /// <param name="toDate">Ngày kết thúc của khoảng thời gian báo cáo</param>
    /// <param name="branchId">ID chi nhánh để filter (nullable)</param>
    /// <param name="limit">Số lượng khách hàng tối đa trả về</param>
    /// <returns>Danh sách khách hàng được sắp xếp theo doanh thu giảm dần, bao gồm thông tin loyalty tier</returns>
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
    /// Lấy xu hướng doanh thu theo từng ngày trong khoảng thời gian
    /// </summary>
    /// <param name="fromDate">Ngày bắt đầu của khoảng thời gian báo cáo</param>
    /// <param name="toDate">Ngày kết thúc của khoảng thời gian báo cáo</param>
    /// <param name="branchId">ID chi nhánh để filter (nullable)</param>
    /// <returns>Danh sách doanh thu và số booking theo từng ngày, được sắp xếp theo thứ tự thời gian</returns>
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

    #endregion Dashboard & Overview Reports

    #region Revenue & Booking Reports

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
                TotalRevenue = g.Sum(i => i.FinalTotal),
                CourtRevenue = g.Sum(i => i.CourtFee),
                ServiceRevenue = g.Sum(i => i.ServiceFee),
                DiscountAmount = g.Sum(i => i.LoyaltyDiscountAmount + i.PromotionDiscountAmount),
                BookingCount = g.Count()
            })
            .FirstOrDefaultAsync();

        var totalRevenue = metrics?.TotalRevenue ?? 0;
        var courtRevenue = metrics?.CourtRevenue ?? 0;
        var serviceRevenue = metrics?.ServiceRevenue ?? 0;
        var discountAmount = metrics?.DiscountAmount ?? 0;
        var bookingCount = metrics?.BookingCount ?? 0;
        var averageBookingValue = bookingCount > 0 ? totalRevenue / bookingCount : 0;

        // Group items theo groupBy parameter
        var items = await GetRevenueItemsAsync(invoicesQuery, groupBy);

        return new RevenueReportDto
        {
            TotalRevenue = totalRevenue,
            CourtRevenue = courtRevenue,
            ServiceRevenue = serviceRevenue,
            DiscountAmount = discountAmount,
            AverageBookingValue = averageBookingValue,
            Items = items
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

    #endregion Revenue & Booking Reports

    #region Calculation Helpers

    /// <summary>
    /// Tính chi tiết occupancy — trả về (Rate, BookedHours, AvailableHours)
    /// Dùng chung cho Dashboard và CourtUtilization để tránh duplicate queries
    /// </summary>
    private async Task<(decimal Rate, decimal BookedHours, decimal AvailableHours)> CalculateOccupancyDetailsAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        var branchesQuery = _context.Branches.AsNoTracking();
        if (branchId.HasValue)
            branchesQuery = branchesQuery.Where(b => b.Id == branchId.Value);

        var branches = await branchesQuery.ToListAsync();
        if (!branches.Any()) return (0, 0, 0);

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

        if (totalAvailableHours == 0) return (0, 0, 0);

        var bookedHours = await _context.BookingCourts
            .AsNoTracking()
            .Where(bc => (bc.Booking.Status == BookingStatus.COMPLETED ||
                          bc.Booking.Status == BookingStatus.IN_PROGRESS) &&
                         bc.Booking.BookingDate >= fromDate &&
                         bc.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bc.Booking.BranchId == branchId.Value))
            .SumAsync(bc => (decimal)(bc.EndTime - bc.StartTime).TotalHours);

        var rate = Math.Round(bookedHours / totalAvailableHours * 100, 1);
        return (rate, bookedHours, totalAvailableHours);
    }

    /// <summary>
    /// Wrapper: chỉ lấy occupancy rate — dùng trong GetDashboardSummary
    /// </summary>
    private async Task<decimal> CalculateOccupancyRateAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        var (rate, _, _) = await CalculateOccupancyDetailsAsync(fromDate, toDate, branchId);
        return rate;
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

    #region Court Utilization

    /// <summary>
    /// Lấy báo cáo sử dụng sân
    /// </summary>
    public async Task<CourtUtilizationReportDto> GetCourtUtilizationReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        // Lấy toàn bộ occupancy details trong 1 lần gọi — không duplicate query
        var (overallOccupancyRate, totalBookedHours, totalAvailableHours) =
            await CalculateOccupancyDetailsAsync(fromDate, toDate, branchId);

        var peakHours = await GetPeakHoursAsync(fromDate, toDate, branchId, true);
        var offPeakHours = await GetPeakHoursAsync(fromDate, toDate, branchId, false);
        var topCourts = await GetTopCourtsByUsageAsync(fromDate, toDate, branchId, 10);
        var items = await GetCourtUtilizationItemsAsync(fromDate, toDate, branchId, groupBy);

        return new CourtUtilizationReportDto
        {
            OverallOccupancyRate = overallOccupancyRate,
            TotalAvailableHours = totalAvailableHours,
            TotalBookedHours = totalBookedHours,
            PeakHours = peakHours,
            OffPeakHours = offPeakHours,
            TopCourts = topCourts,
            Items = items
        };
    }

    private async Task<List<PeakHourDto>> GetPeakHoursAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, bool isPeak)
    {
        // GroupBy trong SQL — tránh kéo toàn bộ dữ liệu về memory
        var hourlyStats = _context.BookingCourts
            .AsNoTracking()
            .Where(bc => (bc.Booking.Status == BookingStatus.COMPLETED ||
                          bc.Booking.Status == BookingStatus.IN_PROGRESS) &&
                         bc.Booking.BookingDate >= fromDate &&
                         bc.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bc.Booking.BranchId == branchId.Value))
            .GroupBy(bc => bc.StartTime.Hour)
            .Select(g => new PeakHourDto
            {
                Hour = g.Key,
                BookingCount = g.Count(),
                OccupancyRate = 0
            });

        return isPeak
            ? await hourlyStats.OrderByDescending(h => h.BookingCount).Take(3).ToListAsync()
            : await hourlyStats.OrderBy(h => h.BookingCount).Take(3).ToListAsync();
    }

    private async Task<List<CourtUtilizationItemDto>> GetTopCourtsByUsageAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, int limit)
    {
        var courtUsage = await _context.BookingCourts
            .AsNoTracking()
            .Where(bc => (bc.Booking.Status == BookingStatus.COMPLETED ||
                          bc.Booking.Status == BookingStatus.IN_PROGRESS) &&
                         bc.Booking.BookingDate >= fromDate &&
                         bc.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bc.Booking.BranchId == branchId.Value))
            .GroupBy(bc => new { bc.CourtId, bc.Court.Name })
            .Select(g => new
            {
                g.Key.CourtId,
                g.Key.Name,
                BookedHours = g.Sum(bc => (decimal)(bc.EndTime - bc.StartTime).TotalHours)
            })
            .OrderByDescending(c => c.BookedHours)
            .Take(limit)
            .ToListAsync();

        // Calculate available hours per court
        var days = (toDate.ToDateTime(TimeOnly.MinValue) - fromDate.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var courtIds = courtUsage.Select(c => c.CourtId).ToList();
        var courts = await _context.Courts
            .AsNoTracking()
            .Where(c => courtIds.Contains(c.Id))
            .Include(c => c.Branch)
            .ToListAsync();

        return courtUsage.Select(cu =>
        {
            var court = courts.FirstOrDefault(c => c.Id == cu.CourtId);
            var operatingHours = court != null
                ? (decimal)(court.Branch.CloseTime - court.Branch.OpenTime).TotalHours * days
                : 0;

            return new CourtUtilizationItemDto
            {
                CourtId = cu.CourtId,
                CourtName = cu.Name,
                BookedHours = cu.BookedHours,
                AvailableHours = operatingHours,
                OccupancyRate = operatingHours > 0
                    ? Math.Round(cu.BookedHours / operatingHours * 100, 1)
                    : 0
            };
        }).ToList();
    }

    private async Task<List<CourtUtilizationItemDto>> GetCourtUtilizationItemsAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        var normalizedGroupBy = string.IsNullOrEmpty(groupBy) ? "court" : groupBy.ToLower();

        if (normalizedGroupBy == "court")
            return await GetTopCourtsByUsageAsync(fromDate, toDate, branchId, 100);

        var validValues = new[] { "court", "branch", "day", "hour" };
        if (!validValues.Contains(normalizedGroupBy))
            throw new AppException(400,
                $"groupBy '{groupBy}' không hợp lệ. Các giá trị hợp lệ: court, branch, day, hour",
                ErrorCodes.BadRequest);

        throw new AppException(400,
            $"groupBy '{groupBy}' chưa được hỗ trợ. Hiện tại chỉ hỗ trợ: court",
            ErrorCodes.BadRequest);
    }

    #endregion

    #region Customer Statistics

    /// <summary>
    /// Lấy báo cáo thống kê khách hàng
    /// </summary>
    public async Task<CustomerStatisticsReportDto> GetCustomerStatisticsReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        // Total customers (all time)
        var totalCustomersQuery = _context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.CUSTOMER);

        // Filter by branch if specified
        if (branchId.HasValue)
        {
            totalCustomersQuery = totalCustomersQuery
                .Where(u => _context.Bookings.Any(b =>
                    b.CustomerId == u.Id && b.BranchId == branchId.Value));
        }

        var totalCustomers = await totalCustomersQuery.CountAsync();

        // New customers in date range
        // - Không filter branch: customer đăng ký trong range
        // - Filter branch: customer có booking ĐẦU TIÊN tại branch nằm trong range (nhất quán với Dashboard)
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
            var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            newCustomers = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.CUSTOMER &&
                            u.CreatedAt >= fromDateTime &&
                            u.CreatedAt <= toDateTime)
                .CountAsync();
        }

        // Repeat customers: có > 1 COMPLETED booking trong date range
        var repeatCustomersQuery = _context.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.COMPLETED &&
                        b.BookingDate >= fromDate &&
                        b.BookingDate <= toDate &&
                        b.CustomerId.HasValue &&
                        (!branchId.HasValue || b.BranchId == branchId.Value))
            .GroupBy(b => b.CustomerId!.Value)
            .Where(g => g.Count() > 1);

        var repeatCustomers = await repeatCustomersQuery.CountAsync();

        // Repeat customer rate
        var repeatCustomerRate = totalCustomers > 0
            ? Math.Round((decimal)repeatCustomers / totalCustomers * 100, 1)
            : 0;

        // Average bookings per customer
        var totalBookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.COMPLETED &&
                        b.CustomerId.HasValue &&
                        (!branchId.HasValue || b.BranchId == branchId.Value))
            .CountAsync();

        var avgBookingsPerCustomer = totalCustomers > 0
            ? Math.Round((decimal)totalBookings / totalCustomers, 1)
            : 0;

        // Average revenue per customer
        var totalRevenue = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.CustomerId.HasValue &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .SumAsync(i => i.FinalTotal);

        var avgRevenuePerCustomer = totalCustomers > 0
            ? Math.Round(totalRevenue / totalCustomers, 0)
            : 0;

        // Loyalty tier distribution
        var loyaltyDistribution = await GetLoyaltyTierDistributionAsync(branchId);

        // Customer acquisition trend
        var acquisitionTrend = await GetCustomerAcquisitionTrendAsync(fromDate, toDate, branchId, groupBy);

        return new CustomerStatisticsReportDto
        {
            TotalCustomers = totalCustomers,
            NewCustomers = newCustomers,
            RepeatCustomers = repeatCustomers,
            RepeatCustomerRate = repeatCustomerRate,
            AverageBookingsPerCustomer = avgBookingsPerCustomer,
            AverageRevenuePerCustomer = avgRevenuePerCustomer,
            LoyaltyTierDistribution = loyaltyDistribution,
            AcquisitionTrend = acquisitionTrend
        };
    }

    private async Task<List<LoyaltyTierDistributionDto>> GetLoyaltyTierDistributionAsync(Guid? branchId)
    {
        var customersQuery = _context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.CUSTOMER);

        if (branchId.HasValue)
        {
            customersQuery = customersQuery
                .Where(u => _context.Bookings.Any(b =>
                    b.CustomerId == u.Id && b.BranchId == branchId.Value));
        }

        var totalCustomers = await customersQuery.CountAsync();

        var tierDistribution = await customersQuery
            .GroupBy(u => u.CustomerLoyalty != null ? u.CustomerLoyalty.Tier.Name : "Bronze")
            .Select(g => new LoyaltyTierDistributionDto
            {
                TierName = g.Key,
                CustomerCount = g.Count(),
                Percentage = 0 // Will calculate below
            })
            .ToListAsync();

        // Calculate percentages
        foreach (var tier in tierDistribution)
        {
            tier.Percentage = totalCustomers > 0
                ? Math.Round((decimal)tier.CustomerCount / totalCustomers * 100, 1)
                : 0;
        }

        return tierDistribution;
    }

    private async Task<List<CustomerAcquisitionTrendDto>> GetCustomerAcquisitionTrendAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var newCustomersQuery = _context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.CUSTOMER &&
                        u.CreatedAt >= fromDateTime &&
                        u.CreatedAt <= toDateTime);

        if (branchId.HasValue)
        {
            newCustomersQuery = newCustomersQuery
                .Where(u => _context.Bookings.Any(b =>
                    b.CustomerId == u.Id && b.BranchId == branchId.Value));
        }

        // Load to memory vì DateOnly.FromDateTime() không thể translate sang SQL
        // EF Core không hỗ trợ DateOnly operations trong query translation
        var data = await newCustomersQuery
            .Select(u => u.CreatedAt.Date)
            .ToListAsync();

        // GroupBy trong memory để convert DateTime.Date sang DateOnly
        var trendData = data
            .GroupBy(d => DateOnly.FromDateTime(d))
            .Select(g => new CustomerAcquisitionTrendDto
            {
                Period = g.Key.ToString("yyyy-MM-dd"),
                NewCustomers = g.Count()
            })
            .OrderBy(t => t.Period)
            .ToList();

        return trendData;
    }

    #endregion

    #region Top Spenders

    /// <summary>
    /// Lấy báo cáo top khách hàng chi tiêu (có pagination)
    /// </summary>
    public async Task<TopSpendersReportDto> GetTopSpendersReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, int page, int pageSize)
    {
        var query = _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        i.Booking.CustomerId.HasValue &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .GroupBy(i => new
            {
                CustomerId = i.Booking.CustomerId!.Value,
                i.Booking.Customer!.FullName,
                i.Booking.Customer.Email,
                i.Booking.Customer.Phone,
                LoyaltyTier = i.Booking.Customer.CustomerLoyalty != null
                    ? i.Booking.Customer.CustomerLoyalty.Tier.Name
                    : "Bronze"
            })
            .Select(g => new TopSpenderDto
            {
                CustomerId = g.Key.CustomerId,
                FullName = g.Key.FullName,
                Email = g.Key.Email,
                Phone = g.Key.Phone!,
                TotalRevenue = g.Sum(i => i.FinalTotal),
                BookingCount = g.Count(),
                LoyaltyTier = g.Key.LoyaltyTier
            })
            .Where(c => c.TotalRevenue > 0)
            .OrderByDescending(c => c.TotalRevenue);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new TopSpendersReportDto
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    #endregion

    #region Service Performance

    /// <summary>
    /// Lấy báo cáo hiệu suất dịch vụ
    /// </summary>
    public async Task<ServicePerformanceReportDto> GetServicePerformanceReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        // Total service revenue
        var totalServiceRevenue = await _context.BookingServices
            .AsNoTracking()
            .Where(bs => bs.Booking.Status == BookingStatus.COMPLETED &&
                         bs.Booking.BookingDate >= fromDate &&
                         bs.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bs.Booking.BranchId == branchId.Value))
            .SumAsync(bs => bs.UnitPrice * bs.Quantity);

        // Gộp totalBookings và totalBookingsWithServices thành 1 query
        var bookingMetrics = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.COMPLETED &&
                        b.BookingDate >= fromDate &&
                        b.BookingDate <= toDate &&
                        (!branchId.HasValue || b.BranchId == branchId.Value))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalBookings = g.Count(),
                WithServices = g.Count(b => b.BookingServices.Any())
            })
            .FirstOrDefaultAsync();

        var totalBookings = bookingMetrics?.TotalBookings ?? 0;
        var totalBookingsWithServices = bookingMetrics?.WithServices ?? 0;

        // Service attachment rate
        var serviceAttachmentRate = totalBookings > 0
            ? Math.Round((decimal)totalBookingsWithServices / totalBookings * 100, 1)
            : 0;

        // Average service revenue per booking
        var avgServiceRevenue = totalBookingsWithServices > 0
            ? Math.Round(totalServiceRevenue / totalBookingsWithServices, 0)
            : 0;

        // Top services
        var topServices = await _context.BookingServices
            .AsNoTracking()
            .Where(bs => bs.Booking.Status == BookingStatus.COMPLETED &&
                         bs.Booking.BookingDate >= fromDate &&
                         bs.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bs.Booking.BranchId == branchId.Value))
            .GroupBy(bs => new { bs.ServiceId, bs.Service.Name })
            .Select(g => new ServiceItemDto
            {
                ServiceId = g.Key.ServiceId,
                ServiceName = g.Key.Name,
                Revenue = g.Sum(bs => bs.UnitPrice * bs.Quantity),
                BookingCount = g.Select(bs => bs.BookingId).Distinct().Count(),
                AverageRevenue = 0 // Will calculate below
            })
            .OrderByDescending(s => s.Revenue)
            .Take(10)
            .ToListAsync();

        // Calculate average revenue
        foreach (var service in topServices)
        {
            service.AverageRevenue = service.BookingCount > 0
                ? Math.Round(service.Revenue / service.BookingCount, 0)
                : 0;
        }

        // Service trend (by day)
        var serviceTrend = await GetServiceTrendAsync(fromDate, toDate, branchId);

        return new ServicePerformanceReportDto
        {
            TotalServiceRevenue = totalServiceRevenue,
            TotalBookingsWithServices = totalBookingsWithServices,
            ServiceAttachmentRate = serviceAttachmentRate,
            AverageServiceRevenuePerBooking = avgServiceRevenue,
            TopServices = topServices,
            ServiceTrend = serviceTrend
        };
    }

    private async Task<List<ServiceTrendDto>> GetServiceTrendAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        var data = await _context.BookingServices
            .AsNoTracking()
            .Where(bs => bs.Booking.Status == BookingStatus.COMPLETED &&
                         bs.Booking.BookingDate >= fromDate &&
                         bs.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bs.Booking.BranchId == branchId.Value))
            .GroupBy(bs => bs.Booking.BookingDate)
            .Select(g => new
            {
                Date = g.Key,
                ServiceRevenue = g.Sum(bs => bs.UnitPrice * bs.Quantity),
                BookingCount = g.Select(bs => bs.BookingId).Distinct().Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        return data.Select(d => new ServiceTrendDto
        {
            Period = d.Date.ToString("yyyy-MM-dd"),
            ServiceRevenue = d.ServiceRevenue,
            BookingCount = d.BookingCount
        }).ToList();
    }

    #endregion

    #region Promotion Effectiveness

    /// <summary>
    /// Lấy báo cáo hiệu quả khuyến mãi
    /// </summary>
    public async Task<PromotionEffectivenessReportDto> GetPromotionEffectivenessReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        // Gộp totalDiscountAmount và totalPromotionUsage thành 1 query
        var promoMetrics = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalDiscount = g.Sum(i => i.PromotionDiscountAmount),
                UsageCount = g.Count(i => i.PromotionDiscountAmount > 0)
            })
            .FirstOrDefaultAsync();

        var totalDiscountAmount = promoMetrics?.TotalDiscount ?? 0;
        var totalPromotionUsage = promoMetrics?.UsageCount ?? 0;

        // Average discount per usage
        var avgDiscountPerUsage = totalPromotionUsage > 0
            ? Math.Round(totalDiscountAmount / totalPromotionUsage, 0)
            : 0;

        // Total bookings
        var totalBookings = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.COMPLETED &&
                        b.BookingDate >= fromDate &&
                        b.BookingDate <= toDate &&
                        (!branchId.HasValue || b.BranchId == branchId.Value))
            .CountAsync();

        // Promotion conversion rate
        var promotionConversionRate = totalBookings > 0
            ? Math.Round((decimal)totalPromotionUsage / totalBookings * 100, 1)
            : 0;

        // Top promotions — query trực tiếp từ BookingPromotion (quan hệ 1-1 với Booking)
        var topPromotions = await _context.Set<BookingPromotion>()
            .AsNoTracking()
            .Where(bp => bp.Booking.Status == BookingStatus.COMPLETED &&
                         bp.Booking.BookingDate >= fromDate &&
                         bp.Booking.BookingDate <= toDate &&
                         (!branchId.HasValue || bp.Booking.BranchId == branchId.Value))
            .GroupBy(bp => new
            {
                bp.PromotionId,
                Name = bp.PromotionNameSnapshot,
                Code = bp.PromotionCodeSnapshot
            })
            .Select(g => new PromotionItemDto
            {
                PromotionId = g.Key.PromotionId,
                PromotionName = g.Key.Name,
                PromotionCode = g.Key.Code ?? "",
                UsageCount = g.Count(),
                TotalDiscount = g.Sum(bp => bp.DiscountAmount),
                RevenueAfterDiscount = 0,
                AverageDiscount = 0
            })
            .OrderByDescending(p => p.UsageCount)
            .Take(10)
            .ToListAsync();

        // Calculate average discount
        foreach (var promo in topPromotions)
        {
            promo.AverageDiscount = promo.UsageCount > 0
                ? Math.Round(promo.TotalDiscount / promo.UsageCount, 0)
                : 0;
        }

        // Promotion trend
        var promotionTrend = await GetPromotionTrendAsync(fromDate, toDate, branchId);

        return new PromotionEffectivenessReportDto
        {
            TotalDiscountAmount = totalDiscountAmount,
            TotalPromotionUsage = totalPromotionUsage,
            AverageDiscountPerUsage = avgDiscountPerUsage,
            PromotionConversionRate = promotionConversionRate,
            TopPromotions = topPromotions,
            PromotionTrend = promotionTrend
        };
    }

    private async Task<List<PromotionTrendDto>> GetPromotionTrendAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId)
    {
        var data = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        i.Booking.BookingDate >= fromDate &&
                        i.Booking.BookingDate <= toDate &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value) &&
                        i.PromotionDiscountAmount > 0)
            .GroupBy(i => i.Booking.BookingDate)
            .Select(g => new
            {
                Date = g.Key,
                UsageCount = g.Count(),
                TotalDiscount = g.Sum(i => i.PromotionDiscountAmount)
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        return data.Select(d => new PromotionTrendDto
        {
            Period = d.Date.ToString("yyyy-MM-dd"),
            UsageCount = d.UsageCount,
            TotalDiscount = d.TotalDiscount
        }).ToList();
    }
    #endregion
}
