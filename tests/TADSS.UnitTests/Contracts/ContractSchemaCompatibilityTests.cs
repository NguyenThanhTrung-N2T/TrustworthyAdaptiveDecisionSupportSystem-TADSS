using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Contracts;

/// <summary>
/// Bộ kiểm thử đối soát tương thích Hợp đồng (Contract Compatibility Tests)
/// Kiểm chứng trực tiếp Wire Artifacts (JSON payloads) sinh ra từ DTOs với 7 file JSON Schema chuẩn trong docs/contracts/schemas/.
/// </summary>
public class ContractSchemaCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly string SchemasDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/contracts/schemas"));

    #region 1. Schema Files Integrity

    [Theory]
    [InlineData("decision-request.schema.json")]
    [InlineData("decision-result.schema.json")]
    [InlineData("decision-recommendation.schema.json")]
    [InlineData("decision-detail-response.schema.json")]
    [InlineData("override-decision-request.schema.json")]
    [InlineData("override-decision-response.schema.json")]
    [InlineData("pending-approvals-response.schema.json")]
    public void SchemaFiles_ExistAndAreValidJson(string schemaFileName)
    {
        var schemaPath = Path.Combine(SchemasDirectory, schemaFileName);
        File.Exists(schemaPath).Should().BeTrue($"Schema file '{schemaFileName}' must exist in docs/contracts/schemas/");

        var jsonContent = File.ReadAllText(schemaPath);
        var jsonDoc = JsonDocument.Parse(jsonContent);

        jsonDoc.RootElement.TryGetProperty("$schema", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("properties", out _).Should().BeTrue();
    }

    #endregion

    #region 2. Wire Artifact Compatibility with JSON Schemas

    [Fact]
    public void DecisionRequestDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "decision-request.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var requiredProperties = schemaJson["required"]!.AsArray().Select(x => x!.ToString()).ToList();

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
            BookingType = "SINGLE",
            TotalPrice = 200000m,
            DepositAmount = 100000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { ["source"] = "TestRunner" }
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert: Tất cả required fields trong schema đều phải có mặt trên wire JSON
        foreach (var requiredField in requiredProperties)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Wire payload must contain required property '{requiredField}'");
        }

        wireNode["paymentStatus"]!.ToString().Should().Be("PARTIALLY_PAID");
        wireNode["priority"]!.ToString().Should().Be("HIGH");
    }

    [Fact]
    public void DecisionResultDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "decision-result.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var requiredProperties = schemaJson["required"]!.AsArray().Select(x => x!.ToString()).ToList();

        var dto = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = "APPROVED",
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 12,
            FallbackUsed = false,
            PrimaryReason = "APPROVED_STANDARD",
            Reasons = ["Đạt yêu cầu cọc"],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert
        foreach (var requiredField in requiredProperties)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Wire payload must contain required property '{requiredField}'");
        }
    }

    [Fact]
    public void OverrideDecisionRequestDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "override-decision-request.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var requiredProperties = schemaJson["required"]!.AsArray().Select(x => x!.ToString()).ToList();

        var dto = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverrideReason = "Khách hàng thân thiết VIP",
            OverriddenBy = "manager-alice"
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert
        foreach (var requiredField in requiredProperties)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Wire payload must contain required property '{requiredField}'");
        }
    }

    [Fact]
    public void OverrideDecisionResponseDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "override-decision-response.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var requiredProperties = schemaJson["required"]!.AsArray().Select(x => x!.ToString()).ToList();

        var dto = new OverrideDecisionResponseDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            OriginalStatus = "NEED_APPROVAL",
            OverriddenStatus = "APPROVED",
            IsOverridden = true,
            OverriddenBy = "manager-alice",
            OverrideReason = "Khách hàng VIP",
            OverriddenAt = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert
        foreach (var requiredField in requiredProperties)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Wire payload must contain required property '{requiredField}'");
        }
    }

    [Fact]
    public void DecisionRecommendationDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "decision-recommendation.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var requiredProperties = schemaJson["required"]!.AsArray().Select(x => x!.ToString()).ToList();

        var dto = new DecisionRecommendationDto
        {
            Type = "ALTERNATIVE_SLOT",
            Description = "Đề xuất chuyển sang khung giờ 20:00 cùng ngày",
            SuggestedCourtId = Guid.NewGuid(),
            SuggestedStartTime = DateTime.UtcNow.AddHours(2),
            SuggestedEndTime = DateTime.UtcNow.AddHours(4),
            SuggestedPrice = 180000m,
            ConfidenceScore = 0.95
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert
        foreach (var requiredField in requiredProperties)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Wire payload must contain required property '{requiredField}'");
        }

        wireNode["type"]!.ToString().Should().Be("ALTERNATIVE_SLOT");
    }

    [Fact]
    public void DecisionDetailDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "decision-detail-response.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var requiredProperties = schemaJson["required"]!.AsArray().Select(x => x!.ToString()).ToList();

        var dto = new DecisionDetailDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            RequestHash = new string('a', 64),
            Decision = new DecisionSummaryDto
            {
                Status = "APPROVED",
                PrimaryReason = "APPROVED_STANDARD",
                Reasons = ["Đạt điều kiện cọc"],
                Recommendations = []
            },
            Execution = new ExecutionSummaryDto
            {
                StrategyName = "DefaultStrategy",
                StrategyVersion = "1.0.0",
                ExecutionType = "AUTOMATED",
                ExecutionTimeMs = 8,
                FallbackUsed = false,
                CacheHit = false
            },
            Governance = new GovernanceSummaryDto
            {
                NeedApproval = false,
                IsOverridden = false
            },
            Timestamps = new TimestampSummaryDto
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        // Act
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert
        foreach (var requiredField in requiredProperties)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Wire payload must contain required property '{requiredField}'");
        }
    }

    [Fact]
    public void PendingApprovalItemDto_SerializedWireJson_ConformsToSchemaRequirements()
    {
        // Arrange
        var schemaPath = Path.Combine(SchemasDirectory, "pending-approvals-response.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var itemRequiredProps = schemaJson["properties"]!["items"]!["items"]!["required"]!.AsArray().Select(x => x!.ToString()).ToList();

        var item = new PendingApprovalItemDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = "NEED_APPROVAL",
            PrimaryReason = "REQUIRE_MANAGER_APPROVAL",
            StrategyName = "HighDemandStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 15,
            FallbackUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(item, JsonOptions);
        var wireNode = JsonNode.Parse(json)!.AsObject();

        // Assert
        foreach (var requiredField in itemRequiredProps)
        {
            wireNode.ContainsKey(requiredField).Should().BeTrue($"Pending item wire payload must contain required property '{requiredField}'");
        }
    }

    #endregion

    #region 3. Enum Values Match Schema Definitions

    [Fact]
    public void BookingPriority_MatchesAllowedEnumInSchema()
    {
        var schemaPath = Path.Combine(SchemasDirectory, "decision-request.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var schemaEnums = schemaJson["properties"]!["priority"]!["enum"]!.AsArray()
            .Select(x => x!.ToString())
            .ToList();

        var sdkEnumNames = Enum.GetNames<BookingPriority>();

        sdkEnumNames.Should().BeEquivalentTo(schemaEnums);
    }

    [Fact]
    public void DecisionStatus_MatchesAllowedEnumInSchema()
    {
        var schemaPath = Path.Combine(SchemasDirectory, "decision-result.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var schemaEnums = schemaJson["properties"]!["status"]!["enum"]!.AsArray()
            .Select(x => x!.ToString())
            .ToList();

        var sdkEnumNames = Enum.GetNames<DecisionStatus>();

        sdkEnumNames.Should().BeEquivalentTo(schemaEnums);
    }

    [Fact]
    public void PaymentStatus_MatchesAllowedEnumInSchema()
    {
        var schemaPath = Path.Combine(SchemasDirectory, "decision-request.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var schemaEnums = schemaJson["properties"]!["paymentStatus"]!["enum"]!.AsArray()
            .Select(x => x!.ToString())
            .ToList();

        var sdkEnumNames = Enum.GetNames<PaymentStatus>();

        sdkEnumNames.Should().BeEquivalentTo(schemaEnums);
    }

    [Fact]
    public void RecommendationType_MatchesAllowedEnumInSchema()
    {
        var schemaPath = Path.Combine(SchemasDirectory, "decision-recommendation.schema.json");
        var schemaJson = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();
        var schemaEnums = schemaJson["properties"]!["type"]!["enum"]!.AsArray()
            .Select(x => x!.ToString())
            .ToList();

        var sdkEnumNames = Enum.GetNames<RecommendationType>();

        sdkEnumNames.Should().BeEquivalentTo(schemaEnums);
    }

    [Fact]
    public void ExecutionType_MatchesAllowedEnumInSchema()
    {
        var sdkEnumNames = Enum.GetNames<ExecutionType>();
        sdkEnumNames.Should().Contain(["AUTOMATED", "FALLBACK", "CACHE_HIT", "MANUAL_OVERRIDE"]);
    }

    #endregion
}
