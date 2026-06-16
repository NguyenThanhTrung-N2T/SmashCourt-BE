using SmashCourt_BE.Integrations.AI.DTOs;

namespace SmashCourt_BE.Services.IService;

public interface IAIService
{
    /// <summary>
    /// Public chat - không cần authentication, chỉ dùng public context
    /// </summary>
    Task<ChatResponseDto> ProcessChatAsync(ChatRequestDto request);

    Task<List<FaqItemDto>> GetFaqListAsync();

    Task<BookingSuggestionResponseDto> GenerateBookingSuggestionsAsync(BookingSuggestionRequestDto request, Guid userId);

    Task<PricingSuggestionResponseDto> GeneratePricingSuggestionsAsync(PricingSuggestionRequestDto request, Guid userId, string userRole);

    Task<PromotionSuggestionResponseDto> GeneratePromotionSuggestionsAsync(PromotionSuggestionRequestDto request, Guid userId, string userRole);

    Task<AnalyticsSummaryResponseDto> GenerateAnalyticsSummaryAsync(AnalyticsSummaryRequestDto request, Guid userId, string userRole);

    Task<StrategicSuggestionResponseDto> GenerateStrategicSuggestionsAsync(StrategicSuggestionRequestDto request, Guid userId);
}
