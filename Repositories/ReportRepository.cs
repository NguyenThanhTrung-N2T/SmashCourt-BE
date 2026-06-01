using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Helpers;
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
        DateOnly fromDate, DateOnly toDate, Guid? branchId, bool isAllTime = false)
    {
        // Tạo base query cho bookings trong khoảng thời gian
        var bookingsQuery = _context.Bookings
            .AsNoTracking();

        if (!isAllTime)
            bookingsQuery = bookingsQuery.Where(b => b.BookingDate >= fromDate && b.BookingDate <= toDate);

        if (branchId.HasValue)
            bookingsQuery = bookingsQuery.Where(b => b.BranchId == branchId.Value);

        // Chỉ tính doanh thu từ booking COMPLETED
        var revenueQuery = _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value));

        if (!isAllTime)
            revenueQuery = revenueQuery.Where(i =>
                i.Booking.BookingDate >= fromDate &&
                i.Booking.BookingDate <= toDate);

        var totalRevenue = await revenueQuery.SumAsync(i => i.FinalTotal);

        // Đếm số lượng bookings theo từng trạng thái
        var bookingStatusCounts = await bookingsQuery
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalBookings = bookingStatusCounts.Sum(s => s.Count);
        var completedBookings = bookingStatusCounts.FirstOrDefault(s => s.Status == BookingStatus.COMPLETED)?.Count ?? 0;
        var cancelledBookings = bookingStatusCounts.FirstOrDefault(s => s.Status == BookingStatus.CANCELLED)?.Count ?? 0;
        var noShowBookings = bookingStatusCounts.FirstOrDefault(s => s.Status == BookingStatus.NO_SHOW)?.Count ?? 0;

        // Logic đếm khách hàng mới
        int newCustomers;
        if (branchId.HasValue)
        {
            var firstBookingQuery = _context.Bookings
                .AsNoTracking()
                .Where(b => b.BranchId == branchId.Value && b.CustomerId.HasValue)
                .GroupBy(b => b.CustomerId!.Value)
                .Select(g => g.Min(b => b.BookingDate));

            if (!isAllTime)
                firstBookingQuery = firstBookingQuery
                    .Where(firstBooking => firstBooking >= fromDate && firstBooking <= toDate);

            newCustomers = await firstBookingQuery.CountAsync();
        }
        else
        {
            var userQuery = _context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.CUSTOMER);

            if (!isAllTime)
            {
                // PostgreSQL yêu cầu DateTime phải có Kind = UTC cho timestamp with time zone
                var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                userQuery = userQuery.Where(u => u.CreatedAt >= fromDateTime && u.CreatedAt <= toDateTime);
            }

            newCustomers = await userQuery.CountAsync();
        }

        var occupancyRate = isAllTime
            ? 0  // occupancy rate không có nghĩa cho ALL TIME
            : await CalculateOccupancyRateAsync(fromDate, toDate, branchId);

        // Phân loại doanh thu theo phương thức thanh toán
        var invoicesWithPaymentMethod = await revenueQuery
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
        var query = GetInvoicesQuery(fromDate, toDate, branchId)
            .Where(i => i.Booking.CustomerId.HasValue);

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
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy = "day", bool isAllTime = false)
    {
        var invoicesQuery = GetInvoicesQuery(fromDate, toDate, branchId, isAllTime);

        var items = await GetRevenueItemsAsync(invoicesQuery, groupBy);

        return items.Select(i => new RevenueTrendDto
        {
            Period = i.Period,
            Revenue = i.Revenue,
            BookingCount = i.BookingCount
        }).ToList();
    }

    /// <summary>
    /// Lấy xu hướng booking theo ngày
    /// </summary>
    public async Task<List<BookingTrendDto>> GetBookingTrendAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, bool isAllTime = false)
    {
        var bookingsQuery = GetBookingsQuery(fromDate, toDate, branchId, isAllTime);
        string groupBy = "dayOfWeek"; // Fixed groupBy day_of_week for the booking chart.
        var items = await GetBookingItemsAsync(bookingsQuery, groupBy);

        return items.Select(i => new BookingTrendDto
        {
            Period = i.Period,
            TotalCount = i.BookingCount,
            CompletedCount = i.CompletedCount
        }).ToList();
    }

    public async Task<ManagerDashboardBranchInfoDto> GetManagerDashboardBranchInfoAsync(Guid branchId)
    {
        var branch = await _context.Branches
            .AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => new ManagerDashboardBranchInfoDto
            {
                BranchId = b.Id,
                BranchName = b.Name,
                TotalCourts = b.Courts.Count(c => c.Status != CourtStatus.INACTIVE && c.Status != CourtStatus.SUSPENDED)
            })
            .FirstOrDefaultAsync();

        if (branch == null)
            throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y chi nhÃ¡nh", ErrorCodes.NotFound);

        return branch;
    }

    public async Task<ManagerDashboardKpiDto> GetManagerDashboardKpisAsync(
        Guid branchId, DateOnly today, DateTime now)
    {
        var nowTime = TimeOnly.FromDateTime(now);
        var upcomingLimit = TimeOnly.FromDateTime(now.AddMinutes(30));

        var revenueToday = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.BranchId == branchId &&
                        i.Booking.BookingDate == today &&
                        i.PaymentStatus == InvoicePaymentStatus.PAID &&
                        !GetCancelledOrRefundedStatuses().Contains(i.Booking.Status))
            .SumAsync(i => i.FinalTotal);

        var courtsInUse = await _context.BookingCourts
            .AsNoTracking()
            .Where(bc => bc.Booking.BranchId == branchId &&
                         bc.Date == today &&
                         bc.Booking.Status == BookingStatus.IN_PROGRESS &&
                         bc.StartTime <= nowTime &&
                         (bc.ActualEndPlayTime ?? bc.EndTime) > nowTime)
            .Select(bc => bc.CourtId)
            .Distinct()
            .CountAsync();

        var todayBookingsCount = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.BranchId == branchId &&
                        b.BookingDate == today &&
                        !GetCancelledOrRefundedStatuses().Contains(b.Status))
            .CountAsync();

        var upcomingCheckInsCount = await _context.BookingCourts
            .AsNoTracking()
            .Where(bc => bc.Booking.BranchId == branchId &&
                         bc.Date == today &&
                         GetCheckInWaitingStatuses().Contains(bc.Booking.Status) &&
                         bc.Booking.CheckedInAt == null &&
                         bc.StartTime > nowTime &&
                         bc.StartTime <= upcomingLimit)
            .Select(bc => bc.BookingId)
            .Distinct()
            .CountAsync();

        var actionCounts = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.BranchId == branchId &&
                        b.BookingDate <= today &&
                        GetManagerActionStatuses().Contains(b.Status))
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var pendingPaymentCount = actionCounts.FirstOrDefault(x => x.Status == BookingStatus.PENDING_PAYMENT)?.Count ?? 0;
        var pendingRefundCount = actionCounts.FirstOrDefault(x => x.Status == BookingStatus.CANCELLED_PENDING_REFUND)?.Count ?? 0;

        return new ManagerDashboardKpiDto
        {
            RevenueToday = revenueToday,
            CourtsInUse = courtsInUse,
            TodayBookingsCount = todayBookingsCount,
            UpcomingCheckInsCount = upcomingCheckInsCount,
            NeedsActionCount = pendingPaymentCount + pendingRefundCount,
            PendingPaymentCount = pendingPaymentCount,
            PendingRefundCount = pendingRefundCount
        };
    }

    public async Task<List<LiveCourtAttentionDto>> GetManagerDashboardLiveCourtsAsync(
        Guid branchId, DateOnly today, DateTime now, int fixedCards = 8)
    {
        var nowTime = TimeOnly.FromDateTime(now);
        var upcomingLimit = TimeOnly.FromDateTime(now.AddMinutes(30));

        var courts = await _context.Courts
            .AsNoTracking()
            .Where(c => c.BranchId == branchId &&
                        c.Status != CourtStatus.INACTIVE &&
                        c.Status != CourtStatus.SUSPENDED)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var courtIds = courts.Select(c => c.Id).ToList();
        var bookingCourts = await _context.BookingCourts
            .AsNoTracking()
            .Include(bc => bc.Court)
            .Include(bc => bc.Booking)
                .ThenInclude(b => b.Customer)
            .Include(bc => bc.Booking)
                .ThenInclude(b => b.Invoice!)
                    .ThenInclude(i => i.Payments)
            .Where(bc => courtIds.Contains(bc.CourtId) &&
                         bc.Date == today &&
                         GetLiveCourtBookingStatuses().Contains(bc.Booking.Status))
            .OrderBy(bc => bc.StartTime)
            .ToListAsync();

        var attentionCards = courts
            .Select(court => BuildLiveCourtCard(court, bookingCourts.Where(bc => bc.CourtId == court.Id).ToList(), now, nowTime, upcomingLimit))
            .Where(card => card.AttentionStatus != "AVAILABLE")
            .OrderBy(GetLiveCourtPriority)
            .ThenBy(card => card.StartTime ?? DateTime.MaxValue)
            .ThenBy(card => card.CourtName)
            .Take(fixedCards)
            .ToList();

        if (attentionCards.Count >= fixedCards)
            return attentionCards;

        var selectedCourtIds = attentionCards.Select(card => card.CourtId).ToHashSet();
        var fillerCards = courts
            .Where(court => !selectedCourtIds.Contains(court.Id) &&
                            court.Status == CourtStatus.AVAILABLE &&
                            !bookingCourts.Any(bc => bc.CourtId == court.Id &&
                                                     bc.StartTime <= nowTime &&
                                                     (bc.ActualEndPlayTime ?? bc.EndTime) > nowTime &&
                                                     bc.Booking.Status == BookingStatus.IN_PROGRESS))
            .OrderBy(court => court.Name)
            .Select(court => new LiveCourtAttentionDto
            {
                CourtId = court.Id,
                CourtName = court.Name,
                CourtStatus = court.Status.ToString(),
                AttentionStatus = "AVAILABLE"
            })
            .Take(fixedCards - attentionCards.Count)
            .ToList();

        attentionCards.AddRange(fillerCards);
        return attentionCards;
    }

    public async Task<List<UpcomingBookingDashboardItemDto>> GetManagerDashboardUpcomingBookingsAsync(
        Guid branchId, DateOnly today, DateTime now, int limit = 10)
    {
        var nowTime = TimeOnly.FromDateTime(now);

        var bookings = await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Customer)
            .Include(b => b.Invoice)
            .Include(b => b.BookingCourts)
                .ThenInclude(bc => bc.Court)
            .Where(b => b.BranchId == branchId &&
                        b.BookingDate == today &&
                        GetCheckInWaitingStatuses().Contains(b.Status) &&
                        b.BookingCourts.Any(bc => bc.Date == today && bc.StartTime >= nowTime))
            .OrderBy(b => b.BookingCourts
                .Where(bc => bc.Date == today && bc.StartTime >= nowTime)
                .Min(bc => bc.StartTime))
            .Take(limit)
            .ToListAsync();

        return bookings
            .Select(ToUpcomingBookingDashboardItem)
            .OrderBy(item => item.StartTime)
            .ToList();
    }

    public async Task<List<ManagerDashboardActionItemDto>> GetManagerDashboardActionQueueAsync(
        Guid branchId, DateOnly today)
    {
        var bookings = await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Customer)
            .Include(b => b.Invoice)
                .ThenInclude(i => i!.Payments)
                    .ThenInclude(p => p.Refunds)
            .Include(b => b.BookingCourts)
                .ThenInclude(bc => bc.Court)
            .Where(b => b.BranchId == branchId &&
                        b.BookingDate <= today &&
                        GetManagerActionStatuses().Contains(b.Status))
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.BookingCourts.Min(bc => bc.StartTime))
            .ToListAsync();

        return bookings.Select(ToManagerDashboardActionItem).ToList();
    }

    public async Task<List<OccupancyForecastPointDto>> GetManagerDashboardOccupancyForecastAsync(
    Guid branchId, DateOnly today, DateTime now, int hours = 8)
    {
        var totalCourts = await _context.Courts
            .AsNoTracking()
            .Where(c => c.BranchId == branchId &&
                        c.Status != CourtStatus.INACTIVE &&
                        c.Status != CourtStatus.SUSPENDED)
            .CountAsync();

        // Vietnam time (single source of truth)
        var vnNow = DateTimeHelper.GetVietnamNow();

        var firstBucketStart = new DateTime(
            vnNow.Year, vnNow.Month, vnNow.Day,
            vnNow.Hour, 0, 0
        );

        var lastBucketEnd = firstBucketStart.AddHours(hours);

        var fromDateTime = firstBucketStart;
        var toDateTime = lastBucketEnd;

        // Load bookings (no Date filtering here anymore)
        var bookingCourts = await _context.BookingCourts
            .AsNoTracking()
            .Where(bc => bc.Booking.BranchId == branchId &&
                         GetForecastBookingStatuses().Contains(bc.Booking.Status))
            .Select(bc => new
            {
                bc.CourtId,
                bc.BookingId,
                Start = bc.Date.ToDateTime(bc.StartTime),
                End = bc.Date.ToDateTime(bc.ActualEndPlayTime ?? bc.EndTime)
            })
            .ToListAsync();

        var result = new List<OccupancyForecastPointDto>(hours);

        for (var i = 0; i < hours; i++)
        {
            var bucketStart = firstBucketStart.AddHours(i);
            var bucketEnd = bucketStart.AddHours(1);

            var overlapping = bookingCourts
                .Where(bc => bc.Start < bucketEnd && bc.End > bucketStart)
                .ToList();

            var occupiedCourts = overlapping
                .Select(x => x.CourtId)
                .Distinct()
                .Count();

            var bookingCount = overlapping
                .Select(x => x.BookingId)
                .Distinct()
                .Count();

            var occupancyRate = totalCourts > 0
                ? Math.Round((decimal)occupiedCourts / totalCourts * 100, 1)
                : 0;

            result.Add(new OccupancyForecastPointDto
            {
                Time = bucketStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                TotalCourts = totalCourts,
                OccupiedCourts = occupiedCourts,
                AvailableCourts = Math.Max(totalCourts - occupiedCourts, 0),
                BookingCount = bookingCount,
                OccupancyRate = occupancyRate,
                IsPeakRisk = occupancyRate >= 80
            });
        }

        return result;
    }
    #endregion Dashboard & Overview Reports

    #region Revenue & Booking Reports

    /// <summary>
    /// Lấy báo cáo doanh thu với grouping
    /// </summary>
    public async Task<RevenueReportDto> GetRevenueReportAsync(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy)
    {
        var invoicesQuery = GetInvoicesQuery(fromDate, toDate, branchId);

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
        var bookingsQuery = GetBookingsQuery(fromDate, toDate, branchId);

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

            return data.Select(d => new RevenueItemDto
            {
                Period = d.Date.ToString("yyyy-MM-dd"),
                Revenue = d.Revenue,
                BookingCount = d.BookingCount
            }).ToList();
        }

        if (normalizedGroupBy == "dayofweek")
        {
            var data = await query
                .GroupBy(i => (int)i.Booking.BookingDate.DayOfWeek)
                .Select(g => new
                {
                    DayOfWeek = g.Key,
                    Revenue = g.Sum(i => i.FinalTotal),
                    BookingCount = g.Count()
                })
                .ToListAsync();

            return data
                .OrderBy(d => d.DayOfWeek == 0 ? 7 : d.DayOfWeek)
                .Select(d => new RevenueItemDto
                {
                    Period = ((DayOfWeek)d.DayOfWeek).ToString(),
                    Revenue = d.Revenue,
                    BookingCount = d.BookingCount
                }).ToList();
        }

        if (normalizedGroupBy == "month")
        {
            var data = await query
                .GroupBy(i => new { i.Booking.BookingDate.Year, i.Booking.BookingDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Revenue = g.Sum(i => i.FinalTotal),
                    BookingCount = g.Count()
                })
                .OrderBy(r => r.Year)
                .ThenBy(r => r.Month)
                .ToListAsync();

            return data.Select(d => new RevenueItemDto
            {
                Period = $"{d.Year:0000}-{d.Month:00}",
                Revenue = d.Revenue,
                BookingCount = d.BookingCount
            }).ToList();
        }

        if (normalizedGroupBy == "week")
        {
            // Group by the Monday of each week
            var data = await query
                .GroupBy(i => i.Booking.BookingDate.AddDays(
                    i.Booking.BookingDate.DayOfWeek == DayOfWeek.Sunday ? -6 : 1 - (int)i.Booking.BookingDate.DayOfWeek))
                .Select(g => new
                {
                    WeekStart = g.Key,
                    Revenue = g.Sum(i => i.FinalTotal),
                    BookingCount = g.Count()
                })
                .OrderBy(w => w.WeekStart)
                .ToListAsync();

            return data.Select(d => new RevenueItemDto
            {
                // Format as "YYYY-W##" or similar. For simplicity, showing week starting date.
                Period = $"{d.WeekStart:yyyy-MM-dd}",
                Revenue = d.Revenue,
                BookingCount = d.BookingCount
            }).ToList();
        }

        if (normalizedGroupBy == "hour")
        {
            var data = await query
                .GroupBy(i => i.Booking.BookingCourts.Min(bc => bc.StartTime).Hour)
                .Select(g => new
                {
                    Hour = g.Key,
                    Revenue = g.Sum(i => i.FinalTotal),
                    BookingCount = g.Count()
                })
                .OrderBy(h => h.Hour)
                .ToListAsync();

            return data.Select(d => new RevenueItemDto
            {
                Period = $"{d.Hour:D2}:00",
                Revenue = d.Revenue,
                BookingCount = d.BookingCount
            }).ToList();
        }

        if (normalizedGroupBy == "courttype")
        {
            var data = await query
                .GroupBy(i => i.Booking.BookingCourts
                    .Select(bc => bc.Court.CourtType.Name)
                    .FirstOrDefault() ?? "Unknown")
                .Select(g => new
                {
                    CourtTypeName = g.Key,
                    Revenue = g.Sum(i => i.FinalTotal),
                    BookingCount = g.Count()
                })
                .OrderByDescending(g => g.Revenue)
                .ToListAsync();

            return data.Select(d => new RevenueItemDto
            {
                Period = d.CourtTypeName,
                Revenue = d.Revenue,
                BookingCount = d.BookingCount
            }).ToList();
        }

        var validValues = new[] { "day", "dayofweek", "month", "week", "branch", "courttype", "paymentmethod", "hour" };
        if (!validValues.Contains(normalizedGroupBy))
            throw new AppException(400,
                $"groupBy '{groupBy}' không hợp lệ. Các giá trị hợp lệ: day, week, month, branch, courtType, paymentMethod, hour, dayOfWeek",
                ErrorCodes.BadRequest);

        throw new AppException(400,
            $"groupBy '{groupBy}' chưa được hỗ trợ. Hiện tại hỗ trợ: day, dayofweek, month, week, hour, courttype",
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

            return data.Select(d => new BookingItemDto
            {
                Period = d.Date.ToString("yyyy-MM-dd"),
                BookingCount = d.BookingCount,
                CompletedCount = d.CompletedCount,
                CancelledCount = d.CancelledCount
            }).ToList();
        }

        if (normalizedGroupBy == "dayofweek")
        {
            var data = await query
                .GroupBy(b => (int)b.BookingDate.DayOfWeek)
                .Select(g => new
                {
                    DayOfWeek = g.Key,
                    BookingCount = g.Count(),
                    CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED),
                    CancelledCount = g.Count(b => b.Status == BookingStatus.CANCELLED)
                })
                .ToListAsync();

            return data
                .OrderBy(d => d.DayOfWeek == 0 ? 7 : d.DayOfWeek)
                .Select(d => new BookingItemDto
                {
                    Period = ((DayOfWeek)d.DayOfWeek).ToString(),
                    BookingCount = d.BookingCount,
                    CompletedCount = d.CompletedCount,
                    CancelledCount = d.CancelledCount
                }).ToList();
        }

        if (normalizedGroupBy == "month")
        {
            var data = await query
                .GroupBy(b => new { b.BookingDate.Year, b.BookingDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    BookingCount = g.Count(),
                    CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED),
                    CancelledCount = g.Count(b => b.Status == BookingStatus.CANCELLED)
                })
                .OrderBy(b => b.Year)
                .ThenBy(b => b.Month)
                .ToListAsync();

            return data.Select(d => new BookingItemDto
            {
                Period = $"{d.Year:0000}-{d.Month:00}",
                BookingCount = d.BookingCount,
                CompletedCount = d.CompletedCount,
                CancelledCount = d.CancelledCount
            }).ToList();
        }

        if (normalizedGroupBy == "week")
        {
            var data = await query
                .GroupBy(b => b.BookingDate.AddDays(
                    b.BookingDate.DayOfWeek == DayOfWeek.Sunday ? -6 : 1 - (int)b.BookingDate.DayOfWeek))
                .Select(g => new
                {
                    WeekStart = g.Key,
                    BookingCount = g.Count(),
                    CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED),
                    CancelledCount = g.Count(b => b.Status == BookingStatus.CANCELLED)
                })
                .OrderBy(w => w.WeekStart)
                .ToListAsync();

            return data.Select(d => new BookingItemDto
            {
                Period = $"{d.WeekStart:yyyy-MM-dd}",
                BookingCount = d.BookingCount,
                CompletedCount = d.CompletedCount,
                CancelledCount = d.CancelledCount
            }).ToList();
        }

        if (normalizedGroupBy == "hour")
        {
            var data = await query
                .GroupBy(b => b.BookingCourts.Min(bc => bc.StartTime).Hour)
                .Select(g => new
                {
                    Hour = g.Key,
                    BookingCount = g.Count(),
                    CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED),
                    CancelledCount = g.Count(b => b.Status == BookingStatus.CANCELLED)
                })
                .OrderBy(h => h.Hour)
                .ToListAsync();

            return data.Select(d => new BookingItemDto
            {
                Period = $"{d.Hour:D2}:00",
                BookingCount = d.BookingCount,
                CompletedCount = d.CompletedCount,
                CancelledCount = d.CancelledCount
            }).ToList();
        }

        if (normalizedGroupBy == "courttype")
        {
            var data = await query
                .GroupBy(b => b.BookingCourts
                    .Select(bc => bc.Court.CourtType.Name)
                    .FirstOrDefault() ?? "Unknown")
                .Select(g => new
                {
                    CourtTypeName = g.Key,
                    BookingCount = g.Count(),
                    CompletedCount = g.Count(b => b.Status == BookingStatus.COMPLETED),
                    CancelledCount = g.Count(b => b.Status == BookingStatus.CANCELLED)
                })
                .OrderByDescending(g => g.BookingCount)
                .ToListAsync();

            return data.Select(d => new BookingItemDto
            {
                Period = d.CourtTypeName,
                BookingCount = d.BookingCount,
                CompletedCount = d.CompletedCount,
                CancelledCount = d.CancelledCount
            }).ToList();
        }

        var validValues = new[] { "day", "dayofweek", "month", "week", "branch", "courttype", "paymentmethod", "hour" };
        if (!validValues.Contains(normalizedGroupBy))
            throw new AppException(400,
                $"groupBy '{groupBy}' không hợp lệ. Các giá trị hợp lệ: day, week, month, branch, courtType, paymentMethod, hour, dayOfWeek",
                ErrorCodes.BadRequest);

        throw new AppException(400,
            $"groupBy '{groupBy}' chưa được hỗ trợ. Hiện tại hỗ trợ: day, dayofweek, month, week, hour, courttype",
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

    #region Manager Dashboard Helpers

    private static BookingStatus[] GetCancelledOrRefundedStatuses() =>
    [
        BookingStatus.CANCELLED,
        BookingStatus.CANCELLED_PENDING_REFUND,
        BookingStatus.CANCELLED_REFUNDED
    ];

    private static BookingStatus[] GetCheckInWaitingStatuses() =>
    [
        BookingStatus.PENDING,
        BookingStatus.CONFIRMED,
        BookingStatus.PAID_ONLINE
    ];

    private static BookingStatus[] GetManagerActionStatuses() =>
    [
        BookingStatus.PENDING_PAYMENT,
        BookingStatus.CANCELLED_PENDING_REFUND
    ];

    private static BookingStatus[] GetLiveCourtBookingStatuses() =>
    [
        BookingStatus.PENDING,
        BookingStatus.CONFIRMED,
        BookingStatus.PAID_ONLINE,
        BookingStatus.PENDING_PAYMENT,
        BookingStatus.IN_PROGRESS
    ];

    private static BookingStatus[] GetForecastBookingStatuses() =>
    [
        BookingStatus.PENDING,
        BookingStatus.CONFIRMED,
        BookingStatus.PAID_ONLINE,
        BookingStatus.IN_PROGRESS
    ];

    private static LiveCourtAttentionDto BuildLiveCourtCard(
        Court court,
        List<BookingCourt> courtBookingCourts,
        DateTime now,
        TimeOnly nowTime,
        TimeOnly upcomingLimit)
    {
        var pendingPayment = courtBookingCourts
            .Where(bc => bc.Booking.Status == BookingStatus.PENDING_PAYMENT)
            .OrderBy(bc => bc.StartTime)
            .FirstOrDefault();

        if (pendingPayment != null)
            return ToLiveCourtAttentionDto(court, pendingPayment, "PENDING_PAYMENT", now, nowTime);

        var upcomingCheckIn = courtBookingCourts
            .Where(bc => GetCheckInWaitingStatuses().Contains(bc.Booking.Status) &&
                         bc.Booking.CheckedInAt == null &&
                         bc.StartTime > nowTime &&
                         bc.StartTime <= upcomingLimit)
            .OrderBy(bc => bc.StartTime)
            .FirstOrDefault();

        if (upcomingCheckIn != null)
            return ToLiveCourtAttentionDto(court, upcomingCheckIn, "UPCOMING_CHECK_IN", now, nowTime);

        var noShowRisk = courtBookingCourts
            .Where(bc => GetCheckInWaitingStatuses().Contains(bc.Booking.Status) &&
                         bc.Booking.CheckedInAt == null &&
                         bc.StartTime <= nowTime &&
                         bc.StartTime.AddMinutes(15) >= nowTime)
            .OrderBy(bc => bc.StartTime)
            .FirstOrDefault();

        if (noShowRisk != null)
            return ToLiveCourtAttentionDto(court, noShowRisk, "NO_SHOW_RISK", now, nowTime);

        var playing = courtBookingCourts
            .Where(bc => bc.Booking.Status == BookingStatus.IN_PROGRESS &&
                         bc.StartTime <= nowTime &&
                         (bc.ActualEndPlayTime ?? bc.EndTime) > nowTime)
            .OrderBy(bc => bc.StartTime)
            .FirstOrDefault();

        if (playing != null)
            return ToLiveCourtAttentionDto(court, playing, "PLAYING", now, nowTime);

        return new LiveCourtAttentionDto
        {
            CourtId = court.Id,
            CourtName = court.Name,
            CourtStatus = court.Status.ToString(),
            AttentionStatus = "AVAILABLE"
        };
    }

    private static LiveCourtAttentionDto ToLiveCourtAttentionDto(
        Court court,
        BookingCourt bookingCourt,
        string attentionStatus,
        DateTime now,
        TimeOnly nowTime)
    {
        var booking = bookingCourt.Booking;
        var startTime = ToDateTime(bookingCourt.Date, bookingCourt.StartTime);
        var endTime = ToDateTime(bookingCourt.Date, bookingCourt.ActualEndPlayTime ?? bookingCourt.EndTime);

        return new LiveCourtAttentionDto
        {
            CourtId = court.Id,
            CourtName = court.Name,
            CourtStatus = court.Status.ToString(),
            AttentionStatus = attentionStatus,
            BookingId = booking.Id,
            BookingCode = booking.BookingCode,
            CustomerName = GetCustomerName(booking),
            CustomerPhone = GetCustomerPhone(booking),
            StartTime = startTime,
            EndTime = endTime,
            MinutesUntilStart = bookingCourt.StartTime > nowTime
                ? (int)Math.Round((startTime - now).TotalMinutes)
                : null,
            MinutesSinceStart = bookingCourt.StartTime <= nowTime
                ? Math.Max((int)Math.Round((now - startTime).TotalMinutes), 0)
                : null,
            AmountDue = booking.Invoice == null ? null : CalculateAmountDue(booking.Invoice),
            PaymentStatus = booking.Invoice?.PaymentStatus.ToString()
        };
    }

    private static UpcomingBookingDashboardItemDto ToUpcomingBookingDashboardItem(Booking booking)
    {
        var courts = ToDashboardCourtSlots(booking);
        return new UpcomingBookingDashboardItemDto
        {
            BookingId = booking.Id,
            BookingCode = booking.BookingCode,
            CustomerName = GetCustomerName(booking),
            CustomerPhone = GetCustomerPhone(booking),
            Courts = courts,
            StartTime = courts.Min(c => c.StartTime),
            EndTime = courts.Max(c => c.EndTime),
            BookingStatus = booking.Status.ToString(),
            PaymentStatus = booking.Invoice?.PaymentStatus.ToString() ?? "",
            FinalTotal = booking.Invoice?.FinalTotal ?? 0
        };
    }

    private static ManagerDashboardActionItemDto ToManagerDashboardActionItem(Booking booking)
    {
        var courts = ToDashboardCourtSlots(booking);
        return new ManagerDashboardActionItemDto
        {
            BookingId = booking.Id,
            BookingCode = booking.BookingCode,
            ActionType = booking.Status.ToString(),
            CustomerName = GetCustomerName(booking),
            CustomerPhone = GetCustomerPhone(booking),
            Courts = courts,
            StartTime = courts.Count == 0 ? null : courts.Min(c => c.StartTime),
            EndTime = courts.Count == 0 ? null : courts.Max(c => c.EndTime),
            Amount = CalculateActionAmount(booking),
            CreatedAt = booking.CreatedAt
        };
    }

    private static List<DashboardCourtSlotDto> ToDashboardCourtSlots(Booking booking)
    {
        return booking.BookingCourts
            .OrderBy(bc => bc.Date)
            .ThenBy(bc => bc.StartTime)
            .Select(bc => new DashboardCourtSlotDto
            {
                CourtId = bc.CourtId,
                CourtName = bc.Court.Name,
                StartTime = ToDateTime(bc.Date, bc.StartTime),
                EndTime = ToDateTime(bc.Date, bc.ActualEndPlayTime ?? bc.EndTime)
            })
            .ToList();
    }

    private static int GetLiveCourtPriority(LiveCourtAttentionDto card) =>
        card.AttentionStatus switch
        {
            "PENDING_PAYMENT" => 0,
            "UPCOMING_CHECK_IN" => 1,
            "NO_SHOW_RISK" => 2,
            "PLAYING" => 3,
            _ => 4
        };

    private static decimal CalculateActionAmount(Booking booking)
    {
        if (booking.Invoice == null)
            return 0;

        if (booking.Status == BookingStatus.CANCELLED_PENDING_REFUND)
        {
            var pendingRefund = booking.Invoice.Payments
                .SelectMany(p => p.Refunds)
                .Where(r => r.Status == RefundStatus.PENDING)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();

            return pendingRefund?.Amount ?? booking.Invoice.FinalTotal;
        }

        return CalculateAmountDue(booking.Invoice);
    }

    private static decimal CalculateAmountDue(Invoice invoice)
    {
        var paidAmount = invoice.Payments
            .Where(p => p.Status == PaymentTxStatus.SUCCESS)
            .Sum(p => p.Amount - p.RefundedAmount);

        return Math.Max(invoice.FinalTotal - paidAmount, 0);
    }

    private static string GetCustomerName(Booking booking) =>
        booking.Customer?.FullName ?? booking.GuestName ?? "Khach vang lai";

    private static string? GetCustomerPhone(Booking booking) =>
        booking.Customer?.Phone ?? booking.GuestPhone;

    private static DateTime ToDateTime(DateOnly date, TimeOnly time) =>
        date.ToDateTime(time);

    #endregion

    #region Helper Query Builders

    private IQueryable<Models.Entities.Invoice> GetInvoicesQuery(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, bool isAllTime = false)
    {
        var query = _context.Invoices
            .AsNoTracking()
            .Where(i => i.Booking.Status == BookingStatus.COMPLETED &&
                        (!branchId.HasValue || i.Booking.BranchId == branchId.Value));

        if (!isAllTime)
            query = query.Where(i => i.Booking.BookingDate >= fromDate && i.Booking.BookingDate <= toDate);

        return query;
    }

    private IQueryable<Models.Entities.Booking> GetBookingsQuery(
        DateOnly fromDate, DateOnly toDate, Guid? branchId, bool isAllTime = false)
    {
        var query = _context.Bookings
            .AsNoTracking()
            .Where(b => !branchId.HasValue || b.BranchId == branchId.Value);

        if (!isAllTime)
            query = query.Where(b => b.BookingDate >= fromDate && b.BookingDate <= toDate);

        return query;
    }

    #endregion
}
