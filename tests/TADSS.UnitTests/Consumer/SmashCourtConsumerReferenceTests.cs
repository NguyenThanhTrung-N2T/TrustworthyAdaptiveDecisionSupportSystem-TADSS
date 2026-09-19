using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Consumer;

/// <summary>
/// Kiểm thử mô phỏng Consumer Reference Testing (Case Study: SmashCourt-BE Consumer).
/// Chứng minh rằng Consumer ứng dụng thực tế có thể sử dụng trơn tru DTOs, Rich Domain Model, Enums và Mappings mà không gặp xung đột kiểu hay phụ thuộc vòng.
/// </summary>
public class SmashCourtConsumerReferenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void SmashCourtConsumer_CanInstantiateAndMapDecisionRequestDto()
    {
        // Arrange: Payload nhận được từ API Gateway / Mobile App của SmashCourt
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

        // Act: Consumer chuyển DTO sang Rich Domain Entity để xử lý nghiệp vụ
        var domain = dto.ToDomain();

        // Assert: Trạng thái và Invariant được bảo vệ
        domain.Should().NotBeNull();
        domain.ReservationId.Should().Be(dto.ReservationId);
        domain.CalculateDepositRatio().Should().Be(0.5m);
        domain.BookingType.Should().Be("SINGLE");
        domain.Priority.Should().Be(BookingPriority.HIGH);
        domain.Payment.PaymentStatus.Should().Be(PaymentStatus.PARTIALLY_PAID);

        // Act: Chuyển đổi ngược về Wire DTO để gửi đi
        var roundtrippedDto = domain.ToDto();
        roundtrippedDto.ReservationId.Should().Be(dto.ReservationId);
        roundtrippedDto.PaymentStatus.Should().Be("PARTIALLY_PAID");
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

        // Act: Consumer dùng ApiResponse<T> và PagedResult<T>
        var apiResponse = ApiResponse<DecisionResultDto>.Ok(sampleData, "Đánh giá thành công");
        var pagedResult = new PagedResult<DecisionResultDto>
        {
            Items = [sampleData],
            Page = 1,
            PageSize = 10,
            TotalItems = 1
        };

        // Assert
        apiResponse.Success.Should().BeTrue();
        apiResponse.Message.Should().Be("Đánh giá thành công");
        apiResponse.Data.Should().NotBeNull();
        pagedResult.TotalItems.Should().Be(1);
        pagedResult.TotalPages.Should().Be(1);
        pagedResult.HasNext.Should().BeFalse();
        pagedResult.HasPrev.Should().BeFalse();
    }
}
