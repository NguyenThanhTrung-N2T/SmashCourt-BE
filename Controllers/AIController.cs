using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Integrations.AI.DTOs;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers;

/// <summary>
/// Controller xử lý các tính năng AI cho chat, gợi ý đặt sân và phân tích.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly IAIService _aiService;

    public AIController(IAIService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("chat")]
    [Authorize(Roles = "CUSTOMER")]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.ProcessChatAsync(request, userId, userRole);
        return Ok(ApiResponse<ChatResponseDto>.Ok(response));
    }

    [HttpGet("chat/faq")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFaqList()
    {
        var response = await _aiService.GetFaqListAsync();
        return Ok(ApiResponse<List<FaqItemDto>>.Ok(response));
    }

    [HttpPost("suggest/booking")]
    [Authorize(Roles = "CUSTOMER")]
    public async Task<IActionResult> SuggestBooking([FromBody] BookingSuggestionRequestDto request)
    {
        var (userId, _) = GetCurrentUser();
        var response = await _aiService.GenerateBookingSuggestionsAsync(request, userId);
        return Ok(ApiResponse<BookingSuggestionResponseDto>.Ok(response));
    }

    [HttpPost("suggest/pricing")]
    [Authorize(Roles = "OWNER,BRANCH_MANAGER")]
    public async Task<IActionResult> SuggestPricing([FromBody] PricingSuggestionRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.GeneratePricingSuggestionsAsync(request, userId, userRole);
        return Ok(ApiResponse<PricingSuggestionResponseDto>.Ok(response));
    }

    [HttpPost("suggest/promotions")]
    [Authorize(Roles = "OWNER,BRANCH_MANAGER")]
    public async Task<IActionResult> SuggestPromotions([FromBody] PromotionSuggestionRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.GeneratePromotionSuggestionsAsync(request, userId, userRole);
        return Ok(ApiResponse<PromotionSuggestionResponseDto>.Ok(response));
    }

    [HttpPost("analytics/summary")]
    [Authorize(Roles = "OWNER,BRANCH_MANAGER")]
    public async Task<IActionResult> GetAnalyticsSummary([FromBody] AnalyticsSummaryRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.GenerateAnalyticsSummaryAsync(request, userId, userRole);
        return Ok(ApiResponse<AnalyticsSummaryResponseDto>.Ok(response));
    }

    [HttpPost("analytics/strategic")]
    [Authorize(Roles = "OWNER")]
    public async Task<IActionResult> GetStrategicSuggestions([FromBody] StrategicSuggestionRequestDto request)
    {
        var (userId, _) = GetCurrentUser();
        var response = await _aiService.GenerateStrategicSuggestionsAsync(request, userId);
        return Ok(ApiResponse<StrategicSuggestionResponseDto>.Ok(response));
    }

    private (Guid UserId, string UserRole) GetCurrentUser()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(role))
        {
            throw new AppException(401, "Invalid authentication claims", ErrorCodes.Unauthorized);
        }

        return (userId, role);
    }
}
