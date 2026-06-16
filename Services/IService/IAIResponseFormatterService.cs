using SmashCourt_BE.Integrations.AI.DTOs;
using SmashCourt_BE.Services.Internal;

namespace SmashCourt_BE.Services.IService;

public interface IAIResponseFormatterService
{
    ChatResponseDto FormatChatResponse(AiChatResponseDto aiResponse);

    BookingSuggestionResponseDto FormatBookingSuggestions(AiBookingSuggestionResponseDto aiResponse);

    PricingSuggestionResponseDto FormatPricingSuggestions(AiPricingSuggestionResponseDto aiResponse);

    PromotionSuggestionResponseDto FormatPromotionSuggestions(AiPromotionSuggestionResponseDto aiResponse);

    AnalyticsSummaryResponseDto FormatAnalyticsSummary(AiAnalyticsSummaryResponseDto aiResponse);

    StrategicSuggestionResponseDto FormatStrategicSuggestions(AiStrategicSuggestionResponseDto aiResponse);

    ChatResponseDto GetFallbackChatResponse();

    BookingSuggestionResponseDto GetFallbackBookingSuggestions();

    PricingSuggestionResponseDto GetFallbackPricingSuggestions();

    PromotionSuggestionResponseDto GetFallbackPromotionSuggestions(Guid branchId);

    AnalyticsSummaryResponseDto GetFallbackAnalyticsSummary();

    StrategicSuggestionResponseDto GetFallbackStrategicSuggestions();
}
