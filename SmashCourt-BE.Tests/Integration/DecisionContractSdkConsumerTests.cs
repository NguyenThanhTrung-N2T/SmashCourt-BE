using System;
using System.Collections.Generic;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Kiểm thử tính tương thích tham chiếu của Consumer SmashCourt-BE đối với Shared.DecisionContractSDK.
/// Đảm bảo SmashCourt-BE có thể sử dụng trơn tru DTOs, Rich Domain Model, Enums và Mappings mà không gặp xung đột kiểu.
/// </summary>
public class DecisionContractSdkConsumerTests
{
    [Fact]
    public void SmashCourtConsumer_CanInstantiateAndMapDecisionRequestDto()
    {
        // Arrange
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            CourtTypeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "GOLD",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            TotalPrice = 300000m,
            DepositAmount = 150000m,
            PaymentStatus = "PARTIALLY_PAID",
            BookingType = "SINGLE",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "SmashCourt-Consumer"
            }
        };

        // Act: DTO -> Domain Entity
        var domain = dto.ToDomain();

        // Assert
        Assert.NotNull(domain);
        Assert.Equal(dto.ReservationId, domain.ReservationId);
        Assert.Equal(0.5m, domain.CalculateDepositRatio());
        Assert.Equal("SINGLE", domain.BookingType);
        Assert.Equal(BookingPriority.HIGH, domain.Priority);
        Assert.Equal(PaymentStatus.PARTIALLY_PAID, domain.Payment.PaymentStatus);

        // Act: Domain -> DTO
        var roundtrippedDto = domain.ToDto();
        Assert.Equal(dto.ReservationId, roundtrippedDto.ReservationId);
        Assert.Equal("PARTIALLY_PAID", roundtrippedDto.PaymentStatus);
    }

    [Fact]
    public void SmashCourtConsumer_CanWrapResponsesWithSdkCommonTypes()
    {
        // Arrange
        var sampleData = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = "APPROVED",
            StrategyName = "DefaultStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 5,
            FallbackUsed = false,
            PrimaryReason = "APPROVED_STANDARD",
            Reasons = ["Đạt yêu cầu cọc"],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        // Act
        var apiResponse = ApiResponse<DecisionResultDto>.Ok(sampleData, "Đánh giá thành công");
        var pagedResult = new PagedResult<DecisionResultDto>
        {
            Items = [sampleData],
            Page = 1,
            PageSize = 10,
            TotalItems = 1
        };

        // Assert
        Assert.True(apiResponse.Success);
        Assert.Equal("Đánh giá thành công", apiResponse.Message);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal(1, pagedResult.TotalItems);
        Assert.Equal(1, pagedResult.TotalPages);
        Assert.False(pagedResult.HasNext);
        Assert.False(pagedResult.HasPrev);
    }
}
