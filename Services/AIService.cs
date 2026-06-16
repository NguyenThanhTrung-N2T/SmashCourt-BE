using SmashCourt_BE.Common;
using SmashCourt_BE.Integrations.AI;
using SmashCourt_BE.Integrations.AI.DTOs;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.AI.DTOs;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services.Internal;

namespace SmashCourt_BE.Services;

/// <summary>
/// Điều phối các luồng AI phía backend: chuẩn bị dữ liệu, gọi FastAPI và chuẩn hóa response an toàn.
/// </summary>
public class AIService : IAIService
{
    private readonly IFastApiClient _fastApiClient;
    private readonly AIDataPreparationService _dataPreparationService;
    private readonly IAIResponseFormatterService _formatter;
    private readonly IUserBranchRepository _userBranchRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ILogger<AIService> _logger;

    public AIService(
        IFastApiClient fastApiClient,
        AIDataPreparationService dataPreparationService,
        IAIResponseFormatterService formatter,
        IUserBranchRepository userBranchRepository,
        IBranchRepository branchRepository,
        ILogger<AIService> logger)
    {
        _fastApiClient = fastApiClient;
        _dataPreparationService = dataPreparationService;
        _formatter = formatter;
        _userBranchRepository = userBranchRepository;
        _branchRepository = branchRepository;
        _logger = logger;
    }

    /// <summary>
    /// Public chat - không cần authentication, chỉ dùng public context.
    /// Context bao gồm: thông tin hệ thống, cách đặt sân, chính sách hủy/thanh toán, FAQ.
    /// KHÔNG dùng booking history, loyalty, customer info.
    /// </summary>
    public async Task<ChatResponseDto> ProcessChatAsync(ChatRequestDto request)
    {
        try
        {
            var context = await _dataPreparationService.BuildPublicChatContextAsync();
            var aiRequest = new ChatRequestForAiDto
            {
                Message = request.Message,
                Context = context,
                SessionId = request.SessionId
            };

            var aiResponse = await _fastApiClient.PostAsync<ChatRequestForAiDto, AiChatResponseDto>(
                "/api/v1/ai/chat",
                aiRequest);

            return aiResponse == null
                ? _formatter.GetFallbackChatResponse()
                : _formatter.FormatChatResponse(aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process public AI chat");
            return _formatter.GetFallbackChatResponse();
        }
    }

    public async Task<List<FaqItemDto>> GetFaqListAsync()
    {
        try
        {
            var response = await _fastApiClient.GetAsync<FastApiFaqListResponse>("/api/v1/ai/chat/faq");
            return response?.Items
                .Select(item => new FaqItemDto
                {
                    Question = item.Question,
                    Category = item.Category,
                    Answer = item.Answer ?? string.Empty  // Map answer if FastAPI provides it
                })
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI FAQ list");
            return [];
        }
    }

    public async Task<BookingSuggestionResponseDto> GenerateBookingSuggestionsAsync(
        BookingSuggestionRequestDto request,
        Guid userId)
    {
        try
        {
            var history = await _dataPreparationService.PrepareBookingHistoryAsync(userId);
            var branch = request.BranchId.HasValue
                ? await _branchRepository.GetByIdAsync(request.BranchId.Value)
                : null;

            var aiRequest = new FastApiBookingSuggestionRequest
            {
                UserId = userId.ToString(),
                BookingHistory = history.Patterns.Select(pattern => new FastApiBookingHistoryItem
                {
                    BookingDate = pattern.BookingDate,
                    BranchName = pattern.BranchName,
                    CourtType = pattern.CourtType,
                    TimeSlots = [pattern.TimeSlot],
                    Status = "COMPLETED",
                    TotalAmount = pattern.Price
                }).ToList(),
                CurrentBranchId = request.BranchId?.ToString(),
                CurrentBranchName = branch?.Name
            };

            var aiResponse = await _fastApiClient.PostAsync<FastApiBookingSuggestionRequest, AiBookingSuggestionResponseDto>(
                "/api/v1/ai/suggest/booking",
                aiRequest);

            return aiResponse == null
                ? _formatter.GetFallbackBookingSuggestions()
                : _formatter.FormatBookingSuggestions(aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate booking suggestions for user {UserId}", userId);
            return _formatter.GetFallbackBookingSuggestions();
        }
    }

    public async Task<PricingSuggestionResponseDto> GeneratePricingSuggestionsAsync(
        PricingSuggestionRequestDto request,
        Guid userId,
        string userRole)
    {
        try
        {
            var branchId = await ResolveBranchIdAsync(request.BranchId, userId, userRole);
            var occupancy = await _dataPreparationService.PrepareOccupancyDataAsync(branchId, request.FromDate, request.ToDate);
            var revenue = await _dataPreparationService.PrepareRevenueDataAsync(branchId, request.FromDate, request.ToDate);
            var branch = branchId.HasValue ? await _branchRepository.GetByIdAsync(branchId.Value) : null;

            var aiRequest = new FastApiPricingSuggestionRequest
            {
                BranchId = branchId?.ToString() ?? "all",
                BranchName = branch?.Name ?? "All Branches",
                Period = $"{request.FromDate:yyyy-MM-dd}..{request.ToDate:yyyy-MM-dd}",
                AverageOccupancyRate = NormalizeRate(occupancy.AverageOccupancyRate),
                OccupancyPatterns = occupancy.Patterns.Select(o => new FastApiOccupancyPatternItem
                {
                    Period = o.Period,
                    OccupancyRate = NormalizeRate(o.OccupancyRate),
                    TotalSlots = o.TotalSlots,
                    BookedSlots = o.BookedSlots
                }).ToList(),
                RevenuePatterns = revenue.Patterns.Select(r => new FastApiRevenuePatternItem
                {
                    Period = r.Period,
                    Revenue = r.Revenue,
                    BookingCount = r.BookingCount,
                    AverageRevenuePerBooking = r.AverageRevenuePerBooking
                }).ToList()
            };

            var aiResponse = await _fastApiClient.PostAsync<FastApiPricingSuggestionRequest, AiPricingSuggestionResponseDto>(
                "/api/v1/ai/suggest/pricing",
                aiRequest);

            return aiResponse == null
                ? _formatter.GetFallbackPricingSuggestions()
                : _formatter.FormatPricingSuggestions(aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate pricing suggestions for user {UserId}", userId);
            return _formatter.GetFallbackPricingSuggestions();
        }
    }

    public async Task<PromotionSuggestionResponseDto> GeneratePromotionSuggestionsAsync(
        PromotionSuggestionRequestDto request,
        Guid userId,
        string userRole)
    {
        try
        {
            var branchId = await ResolveBranchIdAsync(request.BranchId, userId, userRole);
            var occupancy = await _dataPreparationService.PrepareOccupancyDataAsync(branchId, request.FromDate, request.ToDate);
            var revenue = await _dataPreparationService.PrepareRevenueDataAsync(branchId, request.FromDate, request.ToDate);
            var branch = branchId.HasValue ? await _branchRepository.GetByIdAsync(branchId.Value) : null;

            var aiRequest = new FastApiPromotionSuggestionRequest
            {
                BranchId = branchId?.ToString() ?? "all",
                BranchName = branch?.Name ?? "All Branches",
                Period = $"{request.FromDate:yyyy-MM-dd}..{request.ToDate:yyyy-MM-dd}",
                OccupancyPatterns = occupancy.Patterns.Select(o => new FastApiOccupancyPatternItem
                {
                    Period = o.Period,
                    OccupancyRate = NormalizeRate(o.OccupancyRate),
                    TotalSlots = o.TotalSlots,
                    BookedSlots = o.BookedSlots
                }).ToList(),
                RevenuePatterns = revenue.Patterns.Select(r => new FastApiRevenuePatternItem
                {
                    Period = r.Period,
                    Revenue = r.Revenue,
                    BookingCount = r.BookingCount,
                    AverageRevenuePerBooking = r.AverageRevenuePerBooking
                }).ToList()
            };

            var aiResponse = await _fastApiClient.PostAsync<FastApiPromotionSuggestionRequest, AiPromotionSuggestionResponseDto>(
                "/api/v1/ai/suggest/promotions",
                aiRequest);

            return aiResponse == null
                ? _formatter.GetFallbackPromotionSuggestions(branchId ?? Guid.Empty)
                : _formatter.FormatPromotionSuggestions(aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate promotion suggestions for user {UserId}", userId);
            return _formatter.GetFallbackPromotionSuggestions(request.BranchId ?? Guid.Empty);
        }
    }

    public async Task<AnalyticsSummaryResponseDto> GenerateAnalyticsSummaryAsync(
        AnalyticsSummaryRequestDto request,
        Guid userId,
        string userRole)
    {
        try
        {
            var branchId = await ResolveBranchIdAsync(request.BranchId, userId, userRole);
            var dashboard = await _dataPreparationService.PrepareDashboardDataAsync(
                branchId,
                request.FromDate,
                request.ToDate);

            var branch = branchId.HasValue
                ? await _branchRepository.GetByIdAsync(branchId.Value)
                : null;

            var aiRequest = new FastApiAnalyticsRequest
            {
                BranchId = branchId?.ToString() ?? "all",
                BranchName = branch?.Name ?? "All Branches",
                Period = $"{request.FromDate:yyyy-MM-dd}..{request.ToDate:yyyy-MM-dd}",
                Metrics = new FastApiAnalyticsMetrics
                {
                    TotalRevenue = dashboard.Summary.TotalRevenue,
                    TotalBookings = dashboard.Summary.TotalBookings,
                    CancelledBookings = dashboard.Summary.CancelledBookings,
                    CancellationRate = dashboard.Summary.TotalBookings > 0
                        ? (decimal)dashboard.Summary.CancelledBookings / dashboard.Summary.TotalBookings
                        : 0,
                    AvgOccupancyRate = NormalizeRate(dashboard.Summary.OccupancyRate),
                    PeakHours = [],
                    LowHours = [],
                    RevenueByCourtType = [],
                    TopPromotions = []
                }
            };

            var aiResponse = await _fastApiClient.PostAsync<FastApiAnalyticsRequest, AiAnalyticsSummaryResponseDto>(
                "/api/v1/ai/analytics/summary",
                aiRequest);

            return aiResponse == null
                ? _formatter.GetFallbackAnalyticsSummary()
                : _formatter.FormatAnalyticsSummary(aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate analytics summary for user {UserId}", userId);
            return _formatter.GetFallbackAnalyticsSummary();
        }
    }

    public async Task<StrategicSuggestionResponseDto> GenerateStrategicSuggestionsAsync(
        StrategicSuggestionRequestDto request,
        Guid userId)
    {
        try
        {
            var crossBranch = await _dataPreparationService.PrepareCrossBranchDataAsync(request.FromDate, request.ToDate);

            var aiRequest = new FastApiStrategicSuggestionRequest
            {
                Period = $"{request.FromDate:yyyy-MM-dd}..{request.ToDate:yyyy-MM-dd}",
                TotalBranches = crossBranch.TotalBranches,
                TotalRevenue = crossBranch.TotalRevenue,
                TotalBookings = crossBranch.TotalBookings,
                BranchPerformances = crossBranch.BranchPerformance.Select(b => new FastApiStrategicBranchPerformanceItem
                {
                    BranchName = b.BranchName,
                    Revenue = b.Revenue,
                    BookingCount = b.BookingCount,
                    AverageRevenuePerBooking = b.AverageRevenuePerBooking
                }).ToList()
            };

            var aiResponse = await _fastApiClient.PostAsync<FastApiStrategicSuggestionRequest, AiStrategicSuggestionResponseDto>(
                "/api/v1/ai/analytics/strategic",
                aiRequest);

            return aiResponse == null
                ? _formatter.GetFallbackStrategicSuggestions()
                : _formatter.FormatStrategicSuggestions(aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate strategic suggestions for user {UserId}", userId);
            return _formatter.GetFallbackStrategicSuggestions();
        }
    }

    private async Task<Guid?> ResolveBranchIdAsync(Guid? requestedBranchId, Guid userId, string userRole)
    {
        if (userRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var assignment = await _userBranchRepository.GetActiveByUserIdAsync(userId);
            if (assignment == null)
            {
                throw new AppException(403, "Tài khoản quản lý chưa được gán chi nhánh", ErrorCodes.Forbidden);
            }
            return assignment.BranchId;
        }

        return requestedBranchId;
    }

    private static decimal NormalizeRate(decimal rate)
    {
        return rate > 1 ? rate / 100 : rate;
    }
}
