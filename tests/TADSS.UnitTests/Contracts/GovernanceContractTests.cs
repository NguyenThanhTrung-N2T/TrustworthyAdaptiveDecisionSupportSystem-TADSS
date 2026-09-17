using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Contracts;

public class GovernanceContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void OverrideDecisionRequestDto_RoundTrip_PreservesGovernanceFields()
    {
        var dto = DecisionRequestFactory.ValidOverrideRequest();

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<OverrideDecisionRequestDto>(json, JsonOptions);

        deserialized.Should().BeEquivalentTo(dto);
        deserialized!.Decision.Status.Should().Be("APPROVED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    public void OverrideDecisionRequestDto_WithTooShortReason_FailsValidation(string reason)
    {
        var dto = DecisionRequestFactory.ValidOverrideRequest();
        dto.OverrideReason = reason;

        var results = ValidationHelper.Validate(dto);

        results.Should().Contain(
            result => result.MemberNames.Contains(nameof(OverrideDecisionRequestDto.OverrideReason)));
    }

    [Fact]
    public void OverrideDecisionRequestDto_WithReasonOver500Characters_FailsValidation()
    {
        var dto = DecisionRequestFactory.ValidOverrideRequest();
        dto.OverrideReason = new string('x', 501);

        var results = ValidationHelper.Validate(dto);

        results.Should().Contain(
            result => result.MemberNames.Contains(nameof(OverrideDecisionRequestDto.OverrideReason)));
    }

    [Fact]
    public void OverrideDecisionRequestDto_WithReasonExactly500Characters_Passes()
    {
        var dto = DecisionRequestFactory.ValidOverrideRequest();
        dto.OverrideReason = new string('x', 500);

        var results = ValidationHelper.Validate(dto);

        results.Should().BeEmpty();
    }

    [Fact]
    public void OverrideDecisionResponseDto_Serialization_UsesStableWireNames()
    {
        var dto = new OverrideDecisionResponseDto
        {
            DecisionId = Guid.Parse("018e3a2b-9f88-7111-a1b2-001122334455"),
            ReservationId = Guid.Parse("018e3a2b-8a71-7000-8c24-112233445566"),
            OriginalStatus = "NEED_APPROVAL",
            OverriddenStatus = "APPROVED",
            IsOverridden = true,
            OverriddenBy = "manager-usr-001",
            OverrideReason = "Khách hàng thân thiết được quản lý xác nhận ngoại lệ",
            OverriddenAt = new DateTime(2026, 9, 12, 15, 30, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("decisionId", out _).Should().BeTrue();
        root.TryGetProperty("originalStatus", out _).Should().BeTrue();
        root.TryGetProperty("overriddenStatus", out _).Should().BeTrue();
        root.TryGetProperty("isOverridden", out _).Should().BeTrue();
        root.TryGetProperty("overriddenBy", out _).Should().BeTrue();
        root.TryGetProperty("overrideReason", out _).Should().BeTrue();
        root.TryGetProperty("overriddenAt", out _).Should().BeTrue();
        root.TryGetProperty("OriginalStatus", out _).Should().BeFalse();
    }

    [Fact]
    public void PendingApprovalItemDto_Serialization_UsesStableWireNames()
    {
        var dto = new PendingApprovalItemDto
        {
            DecisionId = Guid.Parse("018e3a2b-9f88-7111-a1b2-001122334455"),
            ReservationId = Guid.Parse("018e3a2b-8a71-7000-8c24-112233445566"),
            Status = "NEED_APPROVAL",
            PrimaryReason = "DEPOSIT_BELOW_THRESHOLD",
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 14,
            FallbackUsed = false,
            CreatedAt = new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("decisionId", out _).Should().BeTrue();
        root.TryGetProperty("primaryReason", out _).Should().BeTrue();
        root.TryGetProperty("strategyName", out _).Should().BeTrue();
        root.TryGetProperty("executionTimeMs", out _).Should().BeTrue();
        root.TryGetProperty("fallbackUsed", out _).Should().BeTrue();
        root.TryGetProperty("createdAt", out _).Should().BeTrue();
        root.TryGetProperty("PrimaryReason", out _).Should().BeFalse();
    }
}
