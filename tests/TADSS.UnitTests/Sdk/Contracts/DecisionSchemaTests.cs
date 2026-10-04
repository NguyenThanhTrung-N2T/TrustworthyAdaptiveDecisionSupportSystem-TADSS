using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Sdk.Contracts;

/// <summary>
/// CT-01 (phần schema): JSON schema v2 ở <c>docs/contracts/schemas/v2/</c> chấp nhận payload mẫu và payload do SDK
/// serialize, từ chối payload sai (trường lạ, thời gian thiếu offset, enum sai, trạng thái và cọc/hạn duyệt không khớp).
/// </summary>
public class DecisionSchemaTests
{
    private const string RequestSchema = "decision-request.schema.json";
    private const string ResultSchema = "decision-result.schema.json";

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void RequestSchema_IntegrationSpecSample_IsValid()
    {
        var results = ContractSchemas.Evaluate(RequestSchema, ContractSamples.RequestJson);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void RequestSchema_SdkSerializedGuestRequest_IsValid()
    {
        var guestJson = ContractSamples.MutateRequest(root =>
        {
            var customer = ContractSamples.Object(root["customer"]);
            customer.Remove("customerId");
            customer.Remove("memberSince");
            customer["isGuest"] = true;
            customer["tier"] = "STANDARD";
            root.Remove("demand");
        });
        var serialized = JsonSerializer.Serialize(ContractSamples.DeserializeRequest(guestJson), TadssJson.Options);

        var results = ContractSchemas.Evaluate(RequestSchema, serialized);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void ResultSchema_IntegrationSpecSample_IsValid()
    {
        var results = ContractSchemas.Evaluate(ResultSchema, ContractSamples.ResultJson);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void ResultSchema_SdkSerializedNeedApproval_IsValid()
    {
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.NEED_APPROVAL,
            Deposit = null,
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "2.0.0",
            ExecutionType = ExecutionType.AUTOMATED,
            FallbackUsed = false,
            ReasonCode = ReasonCodes.HighValueReview,
            Reasons = [],
            Recommendations = [],
            ApprovalExpiresAt = new DateTimeOffset(2026, 11, 18, 2, 45, 0, TimeSpan.Zero),
            EvaluatedAt = new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero)
        };

        var results = ContractSchemas.Evaluate(ResultSchema, JsonSerializer.Serialize(result, TadssJson.Options));

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    /// <summary>Quyết định fallback (D-13): <c>APPROVED</c> cọc 100%, <c>executionType = FALLBACK</c>, không hạn duyệt — hợp lệ theo schema.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void ResultSchema_SdkSerializedFallbackFullDeposit_IsValid()
    {
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.APPROVED,
            Deposit = new DepositDto { Rate = 1m, Amount = 228_000m, Currency = "VND" },
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "2.0.0",
            ExecutionType = ExecutionType.FALLBACK,
            FallbackUsed = true,
            ReasonCode = ReasonCodes.FallbackStrategyTimeout,
            Reasons = ["Chế độ an toàn tự động kích hoạt: Chiến lược đánh giá không phản hồi kịp thời hạn cho phép.", "Áp dụng đặt cọc 100% theo chính sách an toàn."],
            Recommendations = [],
            ApprovalExpiresAt = null,
            EvaluatedAt = new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero)
        };

        var results = ContractSchemas.Evaluate(ResultSchema, JsonSerializer.Serialize(result, TadssJson.Options));

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [MemberData(nameof(RequestSchemaNegativeCases.All), MemberType = typeof(RequestSchemaNegativeCases))]
    public void RequestSchema_InvalidPayload_IsRejected(string caseName)
    {
        var json = RequestSchemaNegativeCases.Json(caseName);

        ContractSchemas.Evaluate(RequestSchema, json).IsValid.Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData("approved-without-deposit")]
    [InlineData("need-approval-without-expiry")]
    [InlineData("rejected-with-deposit")]
    public void ResultSchema_StatusInconsistentWithDepositOrExpiry_IsRejected(string caseName)
    {
        var json = ContractSamples.MutateResult(root =>
        {
            switch (caseName)
            {
                case "approved-without-deposit": root["deposit"] = null; break;
                case "need-approval-without-expiry": root["status"] = "NEED_APPROVAL"; root["deposit"] = null; break;
                case "rejected-with-deposit": root["status"] = "REJECTED"; break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        ContractSchemas.Evaluate(ResultSchema, json).IsValid.Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData("approved-with-expiry", "/approvalExpiresAt")]
    [InlineData("rejected-with-expiry", "/approvalExpiresAt")]
    [InlineData("deposit-rate-above-one", "/deposit")]
    public void ResultSchema_ExpiryOrDepositRateOutOfContract_IsRejectedForThatField(string caseName, string expectedLocation)
    {
        var json = ContractSamples.MutateResult(root =>
        {
            switch (caseName)
            {
                case "approved-with-expiry": root["approvalExpiresAt"] = "2026-11-18T02:45:00Z"; break;
                case "rejected-with-expiry": root["status"] = "REJECTED"; root["deposit"] = null; root["approvalExpiresAt"] = "2026-11-18T02:45:00Z"; break;
                case "deposit-rate-above-one": ContractSamples.Object(root["deposit"])["rate"] = 1.5; break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        var results = ContractSchemas.Evaluate(ResultSchema, json);

        results.IsValid.Should().BeFalse();
        ContractSchemas.Describe(results).Should().Contain(expectedLocation, "ca phải bị từ chối đúng ở trường vi phạm, không phải lỗi cú pháp khác");
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [MemberData(nameof(RequestSchemaNegativeCases.DateTimeWithoutExplicitOffset), MemberType = typeof(RequestSchemaNegativeCases))]
    public void RequestSchema_DateTimeWithoutExplicitOffset_IsRejectedEvenWithoutFormatAssertion(string caseName)
    {
        var json = RequestSchemaNegativeCases.Json(caseName);

        ContractSchemas.Evaluate(RequestSchema, json, requireFormatValidation: false).IsValid.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void RequestSchema_IntegrationSpecSample_IsValidWithoutFormatAssertion()
    {
        var results = ContractSchemas.Evaluate(RequestSchema, ContractSamples.RequestJson, requireFormatValidation: false);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }
}
