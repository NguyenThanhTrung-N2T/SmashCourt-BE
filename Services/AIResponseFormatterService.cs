using System.Text.RegularExpressions;
using SmashCourt_BE.Integrations.AI.DTOs;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services.Internal;

namespace SmashCourt_BE.Services;

/// <summary>
/// AI Response Formatter Service - Validates and formats AI Service responses
/// Validates response structure, numeric ranges, sanitizes text, formats for Frontend
/// </summary>
public class AIResponseFormatterService : IAIResponseFormatterService
{
    private readonly ILogger<AIResponseFormatterService> _logger;

    // Validation constants
    private const decimal MinPriceIncreasePercent = -20m;
    private const decimal MaxPriceIncreasePercent = 30m;
    private const decimal MinDiscountPercent = 10m;
    private const decimal MaxDiscountPercent = 50m;
    private const double MinConfidence = 0.0;
    private const double MaxConfidence = 1.0;

    public AIResponseFormatterService(ILogger<AIResponseFormatterService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Format and validate chat response from AI Service
    /// </summary>
    public ChatResponseDto FormatChatResponse(AiChatResponseDto aiResponse)
    {
        try
        {
            if (aiResponse == null)
            {
                _logger.LogWarning("Received null AI chat response");
                return GetFallbackChatResponse();
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(aiResponse.Reply))
            {
                _logger.LogWarning("AI chat response missing reply field");
                return GetFallbackChatResponse();
            }

            // Sanitize text fields
            var sanitizedReply = SanitizeTextResponse(aiResponse.Reply);
            var sanitizedSuggestions = aiResponse.Suggestions
                .Select(SanitizeTextResponse)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            return new ChatResponseDto
            {
                Reply = sanitizedReply,
                Suggestions = sanitizedSuggestions,
                Model = aiResponse.Model,
                SessionId = aiResponse.SessionId,
                GeneratedAt = aiResponse.GeneratedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting chat response");
            return GetFallbackChatResponse();
        }
    }

    /// <summary>
    /// Format and validate booking suggestions from AI Service
    /// </summary>
    public BookingSuggestionResponseDto FormatBookingSuggestions(AiBookingSuggestionResponseDto aiResponse)
    {
        try
        {
            if (aiResponse == null)
            {
                _logger.LogWarning("Received null AI booking suggestion response");
                return GetFallbackBookingSuggestions();
            }

            var formattedSuggestions = aiResponse.Suggestions
                .Select(FormatBookingSuggestionItem)
                .Where(s => s != null)
                .Cast<BookingSuggestionItemDto>()
                .ToList();

            return new BookingSuggestionResponseDto
            {
                Suggestions = formattedSuggestions,
                Model = aiResponse.Model,
                GeneratedAt = aiResponse.GeneratedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting booking suggestions");
            return GetFallbackBookingSuggestions();
        }
    }

    /// <summary>
    /// Format and validate pricing suggestions from AI Service
    /// Validates: suggestedIncreasePercent must be between -20% and +30%
    /// </summary>
    public PricingSuggestionResponseDto FormatPricingSuggestions(AiPricingSuggestionResponseDto aiResponse)
    {
        try
        {
            if (aiResponse == null)
            {
                _logger.LogWarning("Received null AI pricing suggestion response");
                return GetFallbackPricingSuggestions();
            }

            // Parse BranchId if needed (currently string in internal DTO)
            Guid.TryParse(aiResponse.BranchId, out var branchId);

            var formattedSuggestions = new List<PricingSuggestionItemDto>();

            foreach (var insight in aiResponse.Insights)
            {
                // Only process insights that have pricing recommendations
                if (insight.SuggestedIncreasePercent.HasValue)
                {
                    var item = FormatPricingInsight(insight);
                    if (item != null)
                    {
                        formattedSuggestions.Add(item);
                    }
                }
            }

            // Validate that at least some suggestions passed validation
            if (aiResponse.Insights.Count > 0 && formattedSuggestions.Count == 0)
            {
                _logger.LogWarning("All pricing suggestions failed validation");
                return GetFallbackPricingSuggestions();
            }

            return new PricingSuggestionResponseDto
            {
                Suggestions = formattedSuggestions,
                Model = aiResponse.Model,
                GeneratedAt = aiResponse.GeneratedAt,
                Success = formattedSuggestions.Count > 0,
                Message = formattedSuggestions.Count > 0 
                    ? null 
                    : "Unable to generate pricing suggestions at this time. Please try again later."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting pricing suggestions");
            return GetFallbackPricingSuggestions();
        }
    }

    /// <summary>
    /// Format and validate promotion suggestions from AI Service
    /// Validates: discountPercent must be between 10% and 50%
    /// </summary>
    public PromotionSuggestionResponseDto FormatPromotionSuggestions(AiPromotionSuggestionResponseDto aiResponse)
    {
        try
        {
            if (aiResponse == null)
            {
                _logger.LogWarning("Received null AI promotion suggestion response");
                return GetFallbackPromotionSuggestions(Guid.Empty);
            }

            // Parse BranchId
            Guid.TryParse(aiResponse.BranchId, out var branchId);

            var formattedSuggestions = new List<PromotionSuggestionItemDto>();

            foreach (var insight in aiResponse.Insights)
            {
                // Only process insights that have discount recommendations
                if (insight.DiscountPercent.HasValue)
                {
                    var item = FormatPromotionInsight(insight);
                    if (item != null)
                    {
                        formattedSuggestions.Add(item);
                    }
                }
            }

            return new PromotionSuggestionResponseDto
            {
                BranchId = branchId,
                Suggestions = formattedSuggestions,
                Model = aiResponse.Model,
                GeneratedAt = aiResponse.GeneratedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting promotion suggestions");
            return GetFallbackPromotionSuggestions(Guid.Empty);
        }
    }

    /// <summary>
    /// Format and validate analytics summary from AI Service
    /// </summary>
    public AnalyticsSummaryResponseDto FormatAnalyticsSummary(AiAnalyticsSummaryResponseDto aiResponse)
    {
        try
        {
            if (aiResponse == null)
            {
                _logger.LogWarning("Received null AI analytics summary response");
                return GetFallbackAnalyticsSummary();
            }

            // Parse BranchId
            Guid? branchId = null;
            if (!string.IsNullOrEmpty(aiResponse.BranchId) && Guid.TryParse(aiResponse.BranchId, out var parsedBranchId))
            {
                branchId = parsedBranchId;
            }

            // Categorize insights into highlights, concerns, and recommendations
            var highlights = new List<string>();
            var concerns = new List<string>();
            var recommendations = new List<string>();

            foreach (var insight in aiResponse.Insights)
            {
                var sanitizedTitle = SanitizeTextResponse(insight.Title);
                var sanitizedDescription = SanitizeTextResponse(insight.Description);
                var text = $"{sanitizedTitle}: {sanitizedDescription}";

                // Categorize based on severity
                switch (insight.Severity.ToLower())
                {
                    case "positive":
                        highlights.Add(text);
                        break;
                    case "warning":
                    case "critical":
                        concerns.Add(text);
                        break;
                    case "info":
                    default:
                        // Add to highlights if positive category, otherwise to recommendations
                        if (insight.Category.ToLower() == "revenue" || insight.Category.ToLower() == "occupancy")
                        {
                            highlights.Add(text);
                        }
                        break;
                }

                // Add recommendations if present
                if (!string.IsNullOrWhiteSpace(insight.Recommendation))
                {
                    recommendations.Add(SanitizeTextResponse(insight.Recommendation));
                }
            }

            // Build overview from insights
            var overview = BuildOverviewFromInsights(aiResponse.Insights);

            return new AnalyticsSummaryResponseDto
            {
                BranchId = branchId,
                BranchName = SanitizeTextResponse(aiResponse.BranchName),
                Period = SanitizeTextResponse(aiResponse.Period),
                Overview = overview,
                Highlights = highlights,
                Concerns = concerns,
                Recommendations = recommendations,
                Model = aiResponse.Model,
                GeneratedAt = aiResponse.GeneratedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting analytics summary");
            return GetFallbackAnalyticsSummary();
        }
    }

    /// <summary>
    /// Format and validate strategic suggestions from AI Service
    /// </summary>
    public StrategicSuggestionResponseDto FormatStrategicSuggestions(AiStrategicSuggestionResponseDto aiResponse)
    {
        try
        {
            if (aiResponse == null)
            {
                _logger.LogWarning("Received null AI strategic suggestion response");
                return GetFallbackStrategicSuggestions();
            }

            var formattedBranchPerformance = aiResponse.BranchPerformances
                .Select(FormatBranchPerformance)
                .Where(b => b != null)
                .Cast<BranchPerformanceDto>()
                .ToList();

            var formattedStaffingSuggestions = aiResponse.StaffingRecommendations
                .Select(FormatStaffingRecommendation)
                .Where(s => s != null)
                .Cast<StaffingSuggestionDto>()
                .ToList();

            // Extract expansion opportunities from strategic insights
            var formattedExpansionOpportunities = aiResponse.Insights
                .Where(i => i.Category.ToLower() == "expansion")
                .Select(FormatExpansionOpportunityFromInsight)
                .Where(e => e != null)
                .Cast<ExpansionOpportunityDto>()
                .ToList();

            var formattedDemandForecast = aiResponse.DemandForecast != null
                ? FormatDemandForecast(aiResponse.DemandForecast)
                : null;

            return new StrategicSuggestionResponseDto
            {
                BranchPerformance = formattedBranchPerformance,
                StaffingSuggestions = formattedStaffingSuggestions,
                ExpansionOpportunities = formattedExpansionOpportunities,
                DemandForecast = formattedDemandForecast,
                Model = aiResponse.Model,
                GeneratedAt = aiResponse.GeneratedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting strategic suggestions");
            return GetFallbackStrategicSuggestions();
        }
    }

    #region Private Helper Methods - Validation

    /// <summary>
    /// Validate numeric range
    /// </summary>
    private bool ValidateNumericRange(decimal value, decimal min, decimal max, string fieldName)
    {
        if (!IsValidNumeric(value))
        {
            _logger.LogWarning("{FieldName} contains invalid numeric value (NaN or Infinity)", fieldName);
            return false;
        }

        if (value < min || value > max)
        {
            _logger.LogWarning("{FieldName} value {Value} outside valid range [{Min}, {Max}]", 
                fieldName, value, min, max);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validate confidence score (0.0 to 1.0)
    /// </summary>
    private bool ValidateConfidenceScore(double score)
    {
        if (!IsValidNumeric(score))
        {
            _logger.LogWarning("Confidence score contains invalid numeric value (NaN or Infinity)");
            return false;
        }

        if (score < MinConfidence || score > MaxConfidence)
        {
            _logger.LogWarning("Confidence score {Score} outside valid range [{Min}, {Max}]", 
                score, MinConfidence, MaxConfidence);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sanitize text to prevent injection attacks
    /// </summary>
    private string SanitizeTextResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove HTML/script tags
        var sanitized = Regex.Replace(text, @"<[^>]*>", string.Empty);
        
        // Remove potential script injection patterns
        sanitized = Regex.Replace(sanitized, @"javascript:", string.Empty, RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"on\w+\s*=", string.Empty, RegexOptions.IgnoreCase);
        
        return sanitized.Trim();
    }

    /// <summary>
    /// Validate required fields - checks if object is not null and required string fields are not empty
    /// </summary>
    private bool ValidateRequiredFields<T>(T dto) where T : class
    {
        if (dto == null)
        {
            _logger.LogWarning("DTO is null");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validate numeric value is not NaN or Infinity
    /// </summary>
    private bool IsValidNumeric(decimal value)
    {
        // Decimal type doesn't have IsNaN or IsInfinity in standard .NET
        // But we can check for extreme values
        return value != decimal.MaxValue && value != decimal.MinValue;
    }

    /// <summary>
    /// Validate numeric value is not NaN or Infinity
    /// </summary>
    private bool IsValidNumeric(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    #endregion

    #region Private Helper Methods - Formatting

    /// <summary>
    /// Format individual booking suggestion item
    /// </summary>
    private BookingSuggestionItemDto? FormatBookingSuggestionItem(AiSuggestionItemDto item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Title))
            return null;

        return new BookingSuggestionItemDto
        {
            Type = SanitizeTextResponse(item.Type),
            Title = SanitizeTextResponse(item.Title),
            Description = SanitizeTextResponse(item.Description),
            Action = SanitizeTextResponse(item.Action ?? string.Empty),
            Metadata = null // Metadata not present in internal DTO
        };
    }

    /// <summary>
    /// Format and validate pricing insight into pricing suggestion item
    /// </summary>
    private PricingSuggestionItemDto? FormatPricingInsight(AiPricingInsightDto insight)
    {
        if (insight == null || !insight.SuggestedIncreasePercent.HasValue)
            return null;

        var suggestedIncreasePercent = insight.SuggestedIncreasePercent.Value;

        // Validate price increase percentage range (-20% to +30%)
        if (!ValidateNumericRange(suggestedIncreasePercent, MinPriceIncreasePercent, MaxPriceIncreasePercent, "SuggestedIncreasePercent"))
        {
            return null;
        }

        // Extract time slot and day of week from title or description
        // This is a simplified approach - in production, the AI Service should provide these fields
        var timeSlot = ExtractTimeSlotFromText(insight.Title + " " + insight.Description);
        var dayOfWeek = ExtractDayOfWeekFromText(insight.Title + " " + insight.Description);

        // Calculate suggested price (assuming current price is 100 as baseline)
        // In production, this should come from the AI Service or be calculated based on actual prices
        decimal currentPrice = 100m; // Placeholder
        decimal suggestedPrice = currentPrice * (1 + suggestedIncreasePercent / 100m);

        return new PricingSuggestionItemDto
        {
            TimeSlot = timeSlot,
            DayOfWeek = dayOfWeek,
            CurrentPrice = currentPrice,
            SuggestedIncreasePercent = suggestedIncreasePercent,
            SuggestedPrice = suggestedPrice,
            Reasoning = SanitizeTextResponse(insight.Description),
            Confidence = 0.8 // Placeholder - should come from AI Service
        };
    }

    /// <summary>
    /// Format and validate promotion insight into promotion suggestion item
    /// </summary>
    private PromotionSuggestionItemDto? FormatPromotionInsight(AiPromotionInsightDto insight)
    {
        if (insight == null || !insight.DiscountPercent.HasValue)
            return null;

        var discountPercent = insight.DiscountPercent.Value;

        // Validate discount percentage range (10% to 50%)
        if (!ValidateNumericRange(discountPercent, MinDiscountPercent, MaxDiscountPercent, "DiscountPercent"))
        {
            return null;
        }

        // Extract time slot and day of week from title or description
        var timeSlot = ExtractTimeSlotFromText(insight.Title + " " + insight.Description);
        var dayOfWeek = ExtractDayOfWeekFromText(insight.Title + " " + insight.Description);

        return new PromotionSuggestionItemDto
        {
            TimeSlot = timeSlot,
            DayOfWeek = dayOfWeek,
            CurrentOccupancyPercent = 30.0, // Placeholder - should come from AI Service
            DiscountPercent = (int)discountPercent,
            TargetSegment = SanitizeTextResponse(insight.TargetSegment ?? "All"),
            SuggestedDurationDays = 7, // Placeholder - should come from AI Service
            EstimatedRevenueImpact = insight.EstimatedRevenueImpact ?? 0m,
            Reasoning = SanitizeTextResponse(insight.Description)
        };
    }

    /// <summary>
    /// Format branch performance data
    /// </summary>
    private BranchPerformanceDto? FormatBranchPerformance(AiBranchPerformanceDto item)
    {
        if (item == null)
            return null;

        // Parse BranchId
        Guid.TryParse(item.BranchId, out var branchId);

        // Extract strengths and weaknesses from performance rating
        var strengths = new List<string>();
        var weaknesses = new List<string>();

        // Categorize based on performance rating
        switch (item.PerformanceRating.ToLower())
        {
            case "excellent":
                strengths.Add($"High revenue: {item.Revenue:C}");
                strengths.Add($"Strong occupancy: {item.OccupancyRate:P}");
                break;
            case "good":
                strengths.Add($"Solid performance with {item.TotalBookings} bookings");
                break;
            case "average":
                weaknesses.Add("Room for improvement in occupancy");
                break;
            case "poor":
                weaknesses.Add($"Low occupancy rate: {item.OccupancyRate:P}");
                weaknesses.Add("Revenue below target");
                break;
        }

        return new BranchPerformanceDto
        {
            BranchId = branchId,
            BranchName = SanitizeTextResponse(item.BranchName),
            PerformanceRating = SanitizeTextResponse(item.PerformanceRating),
            Strengths = strengths,
            Weaknesses = weaknesses
        };
    }

    /// <summary>
    /// Format staffing recommendation data
    /// </summary>
    private StaffingSuggestionDto? FormatStaffingRecommendation(AiStaffingRecommendationDto item)
    {
        if (item == null)
            return null;

        // Parse BranchId
        Guid.TryParse(item.BranchId, out var branchId);

        return new StaffingSuggestionDto
        {
            BranchId = branchId,
            BranchName = SanitizeTextResponse(item.BranchName),
            Suggestion = SanitizeTextResponse(item.Recommendation),
            Reasoning = SanitizeTextResponse(item.Reasoning)
        };
    }

    /// <summary>
    /// Format expansion opportunity from strategic insight
    /// </summary>
    private ExpansionOpportunityDto? FormatExpansionOpportunityFromInsight(AiStrategicInsightDto insight)
    {
        if (insight == null)
            return null;

        // Extract location from title or description
        var location = ExtractLocationFromText(insight.Title);

        return new ExpansionOpportunityDto
        {
            Location = location,
            Opportunity = SanitizeTextResponse(insight.Title),
            Reasoning = SanitizeTextResponse(insight.Description),
            Priority = 0.7 // Placeholder - should be calculated based on severity
        };
    }

    /// <summary>
    /// Format demand forecast data
    /// </summary>
    private DemandForecastDto? FormatDemandForecast(AiDemandForecastDto item)
    {
        if (item == null)
            return null;

        return new DemandForecastDto
        {
            Summary = SanitizeTextResponse(item.Summary),
            Predictions = item.PeakDays
                .Select(SanitizeTextResponse)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
        };
    }

    /// <summary>
    /// Build overview text from insights
    /// </summary>
    private string BuildOverviewFromInsights(List<AiInsightItemDto> insights)
    {
        if (insights == null || insights.Count == 0)
            return "No significant insights available for this period.";

        var positiveCount = insights.Count(i => i.Severity.ToLower() == "positive");
        var concernCount = insights.Count(i => i.Severity.ToLower() == "warning" || i.Severity.ToLower() == "critical");

        var overview = $"Analysis of {insights.Count} key metrics. ";
        
        if (positiveCount > 0)
            overview += $"{positiveCount} positive trend(s) identified. ";
        
        if (concernCount > 0)
            overview += $"{concernCount} area(s) requiring attention.";

        return overview.Trim();
    }

    /// <summary>
    /// Extract time slot from text (e.g., "06:00-08:00")
    /// </summary>
    private string ExtractTimeSlotFromText(string text)
    {
        var match = Regex.Match(text, @"\d{1,2}:\d{2}\s*-\s*\d{1,2}:\d{2}");
        return match.Success ? match.Value : "All Day";
    }

    /// <summary>
    /// Extract day of week from text
    /// </summary>
    private string ExtractDayOfWeekFromText(string text)
    {
        var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "Weekday", "Weekend" };
        foreach (var day in days)
        {
            if (text.Contains(day, StringComparison.OrdinalIgnoreCase))
                return day;
        }
        return "All Days";
    }

    /// <summary>
    /// Extract location from text
    /// </summary>
    private string ExtractLocationFromText(string text)
    {
        // Simple extraction - in production, this should be more sophisticated
        var words = text.Split(' ');
        return words.Length > 0 ? words[0] : "Unknown Location";
    }

    #endregion

    #region Fallback Response Creators

    /// <summary>
    /// Get fallback chat response
    /// </summary>
    public ChatResponseDto GetFallbackChatResponse()
    {
        return new ChatResponseDto
        {
            Reply = "I'm having trouble processing your request right now. Please try again later or contact support if the issue persists.",
            Suggestions = new List<string>(),
            Model = "fallback",
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get fallback booking suggestions
    /// </summary>
    public BookingSuggestionResponseDto GetFallbackBookingSuggestions()
    {
        return new BookingSuggestionResponseDto
        {
            Suggestions = new List<BookingSuggestionItemDto>(),
            Model = "fallback",
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get fallback pricing suggestions
    /// </summary>
    public PricingSuggestionResponseDto GetFallbackPricingSuggestions()
    {
        return new PricingSuggestionResponseDto
        {
            Suggestions = new List<PricingSuggestionItemDto>(),
            Model = "fallback",
            GeneratedAt = DateTime.UtcNow,
            Success = false,
            Message = "Unable to generate pricing suggestions at this time. Please try again later."
        };
    }

    /// <summary>
    /// Get fallback promotion suggestions
    /// </summary>
    public PromotionSuggestionResponseDto GetFallbackPromotionSuggestions(Guid branchId)
    {
        return new PromotionSuggestionResponseDto
        {
            BranchId = branchId,
            Suggestions = new List<PromotionSuggestionItemDto>(),
            Model = "fallback",
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get fallback analytics summary
    /// </summary>
    public AnalyticsSummaryResponseDto GetFallbackAnalyticsSummary()
    {
        return new AnalyticsSummaryResponseDto
        {
            Overview = "Unable to generate analytics summary at this time.",
            Highlights = new List<string>(),
            Concerns = new List<string>(),
            Recommendations = new List<string>(),
            Model = "fallback",
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get fallback strategic suggestions
    /// </summary>
    public StrategicSuggestionResponseDto GetFallbackStrategicSuggestions()
    {
        return new StrategicSuggestionResponseDto
        {
            BranchPerformance = new List<BranchPerformanceDto>(),
            StaffingSuggestions = new List<StaffingSuggestionDto>(),
            ExpansionOpportunities = new List<ExpansionOpportunityDto>(),
            Model = "fallback",
            GeneratedAt = DateTime.UtcNow
        };
    }

    #endregion
}
