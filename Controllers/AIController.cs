using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>
    /// Public AI Chat - Chatbot hỗ trợ chung cho tất cả người dùng (không cần đăng nhập)
    /// </summary>
    /// <remarks>
    /// **Scope**: Public AI
    /// 
    /// **Authentication**: AllowAnonymous - Không cần đăng nhập
    /// 
    /// **Rate Limiting**: 100 requests/minute (ai-public policy)
    /// 
    /// **Context**: Public context only
    /// - System info (SmashCourt, services)
    /// - Booking process (how to book)
    /// - Cancellation policy
    /// - Payment methods
    /// - FAQ
    /// 
    /// **Security**:
    /// - NO user-specific data
    /// - NO booking history
    /// - NO loyalty info
    /// - NO internal IDs
    /// 
    /// **SessionId**:
    /// - Optional field for conversation context tracking
    /// - Frontend creates UUID and sends with each message
    /// - Only works if FastAPI implements session management
    /// - If not implemented: each request is stateless
    /// 
    /// **Example Request**:
    /// ```json
    /// {
    ///   "message": "Làm thế nào để đặt sân online?",
    ///   "sessionId": "550e8400-e29b-41d4-a716-446655440000"
    /// }
    /// ```
    /// 
    /// **Example Response**:
    /// ```json
    /// {
    ///   "success": true,
    ///   "data": {
    ///     "reply": "Để đặt sân online, bạn có thể...",
    ///     "suggestions": ["Xem bảng giá", "Đặt sân ngay"],
    ///     "model": "gemini-2.0-flash",
    ///     "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    ///     "generatedAt": "27/05/2026 10:30:00"
    ///   }
    /// }
    /// ```
    /// </remarks>
    [HttpPost("chat")]
    [AllowAnonymous]
    [EnableRateLimiting("ai-public")]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request)
    {
        var response = await _aiService.ProcessChatAsync(request);
        return Ok(ApiResponse<ChatResponseDto>.Ok(response));
    }

    /// <summary>
    /// Lấy danh sách câu hỏi thường gặp (FAQ) từ AI
    /// </summary>
    /// <remarks>
    /// **Scope**: Public AI
    /// 
    /// **Authentication**: AllowAnonymous - Không cần đăng nhập
    /// 
    /// **Rate Limiting**: 100 requests/minute (ai-public policy)
    /// 
    /// **Purpose**: Hiển thị danh sách câu hỏi thường gặp để user có thể click vào
    /// 
    /// **Example Response**:
    /// ```json
    /// {
    ///   "success": true,
    ///   "data": [
    ///     {
    ///       "question": "Làm sao để đặt sân?",
    ///       "category": "booking",
    ///       "answer": "Bạn có thể đặt sân qua..."
    ///     },
    ///     {
    ///       "question": "Giá thuê sân là bao nhiêu?",
    ///       "category": "pricing",
    ///       "answer": "Giá thuê sân dao động từ..."
    ///     }
    ///   ]
    /// }
    /// ```
    /// </remarks>
    [HttpGet("chat/faq")]
    [AllowAnonymous]
    [EnableRateLimiting("ai-public")]
    public async Task<IActionResult> GetFaqList()
    {
        var response = await _aiService.GetFaqListAsync();
        return Ok(ApiResponse<List<FaqItemDto>>.Ok(response));
    }

    /// <summary>
    /// Gợi ý đặt sân dựa trên lịch sử booking của khách hàng
    /// </summary>
    /// <remarks>
    /// **Scope**: Customer Personalized AI
    /// 
    /// **Authentication**: CUSTOMER role required
    /// 
    /// **Rate Limiting**: 20 requests/minute (ai-user policy)
    /// 
    /// **Context**: Booking history của chính user
    /// - Branch preferences
    /// - Court type preferences
    /// - Time slot patterns
    /// - Day of week patterns
    /// - Price range
    /// 
    /// **Security**:
    /// - Only user's own booking history
    /// - NO internal IDs sent to AI (BookingId, CourtId, PaymentId)
    /// - Only patterns: BranchName, CourtType, TimeSlot, Price
    /// - NO PII (email, phone, full name)
    /// 
    /// **Request Fields** (all optional):
    /// - `branchId`: Filter suggestions by branch (optional)
    /// - `date`: Target date for booking (optional)
    /// - `courtType`: Preferred court type as TEXT (e.g., "VIP", "Badminton") (optional)
    /// 
    /// **AI Behavior**:
    /// - If fields provided: AI uses them as constraints
    /// - If fields missing: AI infers from booking history
    /// - If no history: AI returns generic suggestions
    /// 
    /// **Example Request**:
    /// ```json
    /// {
    ///   "branchId": "8e441a54-aa47-4da5-a7a4-8784f57c3ae5",
    ///   "date": "2026-05-28",
    ///   "courtType": "VIP"
    /// }
    /// ```
    /// 
    /// **Example Response**:
    /// ```json
    /// {
    ///   "success": true,
    ///   "data": {
    ///     "suggestions": [
    ///       {
    ///         "type": "time_slot",
    ///         "title": "Khung giờ phù hợp",
    ///         "description": "Bạn thường chơi vào 18:00-20:00 cuối tuần",
    ///         "action": "Đặt ngay"
    ///       }
    ///     ],
    ///     "model": "gemini-2.0-flash"
    ///   }
    /// }
    /// ```
    /// </remarks>
    [HttpPost("suggest/booking")]
    [Authorize(Roles = "CUSTOMER")]
    [EnableRateLimiting("ai-user")]
    public async Task<IActionResult> SuggestBooking([FromBody] BookingSuggestionRequestDto request)
    {
        var (userId, _) = GetCurrentUser();
        var response = await _aiService.GenerateBookingSuggestionsAsync(request, userId);
        return Ok(ApiResponse<BookingSuggestionResponseDto>.Ok(response));
    }

    /// <summary>
    /// Gợi ý điều chỉnh giá dựa trên dữ liệu phân tích
    /// </summary>
    /// <remarks>
    /// **Scope**: Manager/Owner Analytics AI
    /// 
    /// **Authentication**: OWNER or BRANCH_MANAGER role required
    /// 
    /// **Rate Limiting**: 10 requests/minute (ai-management policy)
    /// 
    /// **Branch Scoping**:
    /// - BRANCH_MANAGER: Forced to their assigned branch (cannot access other branches)
    /// - OWNER: Can access any branch or all branches
    /// 
    /// **Context**: Aggregate metrics only
    /// - Occupancy rate by time slot
    /// - Revenue trends
    /// - Booking patterns
    /// - NO raw booking data
    /// - NO customer information
    /// 
    /// **AI Suggestions**:
    /// - Increase price for peak hours
    /// - Decrease price for low-demand hours
    /// - Keep price if data insufficient
    /// 
    /// **Validation**:
    /// - priceChangePercentage: [-20%, +30%]
    /// - confidence: [0, 1]
    /// 
    /// **Note**: Output is suggestion only - does NOT auto-update prices
    /// 
    /// **Example Request**:
    /// ```json
    /// {
    ///   "branchId": "8e441a54-aa47-4da5-a7a4-8784f57c3ae5",
    ///   "fromDate": "2026-05-01",
    ///   "toDate": "2026-05-31"
    /// }
    /// ```
    /// </remarks>
    [HttpPost("suggest/pricing")]
    [Authorize(Roles = "OWNER,BRANCH_MANAGER")]
    [EnableRateLimiting("ai-management")]
    public async Task<IActionResult> SuggestPricing([FromBody] PricingSuggestionRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.GeneratePricingSuggestionsAsync(request, userId, userRole);
        return Ok(ApiResponse<PricingSuggestionResponseDto>.Ok(response));
    }

    /// <summary>
    /// Gợi ý khuyến mãi dựa trên dữ liệu phân tích
    /// </summary>
    /// <remarks>
    /// **Scope**: Manager/Owner Analytics AI
    /// 
    /// **Authentication**: OWNER or BRANCH_MANAGER role required
    /// 
    /// **Rate Limiting**: 10 requests/minute (ai-management policy)
    /// 
    /// **Branch Scoping**:
    /// - BRANCH_MANAGER: Forced to their assigned branch
    /// - OWNER: Can access any branch or all branches
    /// 
    /// **Context**: Aggregate metrics only
    /// - Low-demand time slots
    /// - Day of week patterns
    /// - Loyalty tier distribution
    /// - NO customer personal data
    /// 
    /// **AI Suggestions**:
    /// - Discount for low-demand hours
    /// - Day-of-week promotions
    /// - Loyalty tier promotions
    /// 
    /// **Validation**:
    /// - discountPercentage: [10%, 50%]
    /// - confidence: [0, 1]
    /// 
    /// **Note**: Output is suggestion only - does NOT auto-create promotions
    /// 
    /// **Example Request**:
    /// ```json
    /// {
    ///   "branchId": "8e441a54-aa47-4da5-a7a4-8784f57c3ae5",
    ///   "fromDate": "2026-05-01",
    ///   "toDate": "2026-05-31"
    /// }
    /// ```
    /// </remarks>
    [HttpPost("suggest/promotions")]
    [Authorize(Roles = "OWNER,BRANCH_MANAGER")]
    [EnableRateLimiting("ai-management")]
    public async Task<IActionResult> SuggestPromotions([FromBody] PromotionSuggestionRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.GeneratePromotionSuggestionsAsync(request, userId, userRole);
        return Ok(ApiResponse<PromotionSuggestionResponseDto>.Ok(response));
    }

    /// <summary>
    /// Tóm tắt báo cáo bằng ngôn ngữ tự nhiên
    /// </summary>
    /// <remarks>
    /// **Scope**: Manager/Owner Analytics AI
    /// 
    /// **Authentication**: OWNER or BRANCH_MANAGER role required
    /// 
    /// **Rate Limiting**: 10 requests/minute (ai-management policy)
    /// 
    /// **Branch Scoping**:
    /// - BRANCH_MANAGER: Forced to their assigned branch
    /// - OWNER: Can view all branches or specific branch
    /// 
    /// **Context**: Aggregate metrics only
    /// - Total revenue
    /// - Total bookings
    /// - Completed/cancelled bookings
    /// - Cancellation rate
    /// - Occupancy rate
    /// - Peak/low hours
    /// - NO raw booking data
    /// - NO customer information
    /// 
    /// **AI Output**: Natural language summary of business performance
    /// 
    /// **Example Request**:
    /// ```json
    /// {
    ///   "branchId": "8e441a54-aa47-4da5-a7a4-8784f57c3ae5",
    ///   "fromDate": "2026-05-01",
    ///   "toDate": "2026-05-31"
    /// }
    /// ```
    /// 
    /// **Example Response**:
    /// ```json
    /// {
    ///   "success": true,
    ///   "data": {
    ///     "summary": "Trong tháng 5, chi nhánh đạt doanh thu 150 triệu VNĐ...",
    ///     "insights": [
    ///       "Tỷ lệ lấp đầy cao nhất vào cuối tuần (85%)",
    ///       "Giờ cao điểm: 18:00-21:00"
    ///     ],
    ///     "model": "gemini-2.0-flash"
    ///   }
    /// }
    /// ```
    /// </remarks>
    [HttpPost("analytics/summary")]
    [Authorize(Roles = "OWNER,BRANCH_MANAGER")]
    [EnableRateLimiting("ai-management")]
    public async Task<IActionResult> GetAnalyticsSummary([FromBody] AnalyticsSummaryRequestDto request)
    {
        var (userId, userRole) = GetCurrentUser();
        var response = await _aiService.GenerateAnalyticsSummaryAsync(request, userId, userRole);
        return Ok(ApiResponse<AnalyticsSummaryResponseDto>.Ok(response));
    }

    /// <summary>
    /// Gợi ý chiến lược toàn hệ thống (chỉ dành cho OWNER)
    /// </summary>
    /// <remarks>
    /// **Scope**: Owner Strategic AI
    /// 
    /// **Authentication**: OWNER role ONLY (BRANCH_MANAGER cannot access)
    /// 
    /// **Rate Limiting**: 10 requests/minute (ai-management policy)
    /// 
    /// **Context**: System-wide aggregate data
    /// - Cross-branch performance comparison
    /// - Regional demand patterns
    /// - System-wide trends
    /// - NO branch-specific sensitive data
    /// - NO customer information
    /// 
    /// **AI Analysis**:
    /// - Compare branch performance
    /// - Forecast demand by region
    /// - Suggest operating hours expansion
    /// - Suggest staffing adjustments
    /// - Identify underperforming branches
    /// 
    /// **Example Request**:
    /// ```json
    /// {
    ///   "fromDate": "2026-05-01",
    ///   "toDate": "2026-05-31"
    /// }
    /// ```
    /// 
    /// **Example Response**:
    /// ```json
    /// {
    ///   "success": true,
    ///   "data": {
    ///     "recommendations": [
    ///       {
    ///         "title": "Tăng nhân sự cuối tuần tại chi nhánh Quận 1",
    ///         "reason": "Tỷ lệ lấp đầy cao và thời gian checkout tăng",
    ///         "priority": "HIGH",
    ///         "confidence": 0.82
    ///       }
    ///     ],
    ///     "model": "gemini-2.0-flash"
    ///   }
    /// }
    /// ```
    /// </remarks>
    [HttpPost("analytics/strategic")]
    [Authorize(Roles = "OWNER")]
    [EnableRateLimiting("ai-management")]
    public async Task<IActionResult> GetStrategicSuggestions([FromBody] StrategicSuggestionRequestDto request)
    {
        var (userId, _) = GetCurrentUser();
        var response = await _aiService.GenerateStrategicSuggestionsAsync(request, userId);
        return Ok(ApiResponse<StrategicSuggestionResponseDto>.Ok(response));
    }

    /// <summary>
    /// Helper method để lấy userId và userRole từ JWT claims
    /// </summary>
    /// <returns>Tuple (UserId, UserRole)</returns>
    /// <exception cref="AppException">Throw 401 nếu claims không hợp lệ</exception>
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
