using SmashCourt_BE.Common;
using SmashCourt_BE.Integrations.AI;
using SmashCourt_BE.Integrations.AI.DTOs;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
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
        var branchId = await ResolveBranchIdAsync(request.BranchId, userId, userRole);
        var fallback = _formatter.GetFallbackPricingSuggestions();
        fallback.Message = "AI pricing suggestions are not available yet because the Python AI service has no pricing endpoint.";
        return fallback;
    }

    public async Task<PromotionSuggestionResponseDto> GeneratePromotionSuggestionsAsync(
        PromotionSuggestionRequestDto request,
        Guid userId,
        string userRole)
    {
        var branchId = await ResolveBranchIdAsync(request.BranchId, userId, userRole);
        return _formatter.GetFallbackPromotionSuggestions(branchId ?? Guid.Empty);
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

    public Task<StrategicSuggestionResponseDto> GenerateStrategicSuggestionsAsync(
        StrategicSuggestionRequestDto request,
        Guid userId)
    {
        return Task.FromResult(_formatter.GetFallbackStrategicSuggestions());
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

    private sealed class FastApiFaqListResponse
    {
        public List<FastApiFaqItem> Items { get; set; } = [];

        public int Total { get; set; }
    }

    private sealed class FastApiFaqItem
    {
        public string Id { get; set; } = string.Empty;

        public string Question { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string? Answer { get; set; }
    }

    private sealed class FastApiBookingSuggestionRequest
    {
        public string UserId { get; set; } = string.Empty;

        public string? UserName { get; set; }

        public List<FastApiBookingHistoryItem> BookingHistory { get; set; } = [];

        public string? CurrentBranchId { get; set; }

        public string? CurrentBranchName { get; set; }
    }

    private sealed class FastApiBookingHistoryItem
    {
        public string BookingDate { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CourtType { get; set; } = string.Empty;

        public List<string> TimeSlots { get; set; } = [];

        public string Status { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; }
    }

    private sealed class FastApiAnalyticsRequest
    {
        public string BranchId { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string Period { get; set; } = string.Empty;

        public FastApiAnalyticsMetrics Metrics { get; set; } = new();
    }

    private sealed class FastApiAnalyticsMetrics
    {
        public decimal TotalRevenue { get; set; }

        public int TotalBookings { get; set; }

        public int CancelledBookings { get; set; }

        public decimal CancellationRate { get; set; }

        public decimal AvgOccupancyRate { get; set; }

        public List<string> PeakHours { get; set; } = [];

        public List<string> LowHours { get; set; } = [];

        public List<object> RevenueByCourtType { get; set; } = [];

        public List<object> TopPromotions { get; set; } = [];
    }
}
