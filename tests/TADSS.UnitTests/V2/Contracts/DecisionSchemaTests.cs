using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

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
        var results = ContractSchemas.Evaluate(RequestSchema, V2Samples.RequestJson);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void RequestSchema_SdkSerializedGuestRequest_IsValid()
    {
        var guestJson = V2Samples.MutateRequest(root =>
        {
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("customerId");
            customer.Remove("memberSince");
            customer["isGuest"] = true;
            customer["tier"] = "STANDARD";
            root.Remove("demand");
        });
        var serialized = JsonSerializer.Serialize(V2Samples.DeserializeRequest(guestJson), TadssJson.Options);

        var results = ContractSchemas.Evaluate(RequestSchema, serialized);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void ResultSchema_IntegrationSpecSample_IsValid()
    {
        var results = ContractSchemas.Evaluate(ResultSchema, V2Samples.ResultJson);

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
            ExecutionType = ExecutionType.FALLBACK,
            FallbackUsed = true,
            ReasonCode = ReasonCodes.FallbackStrategyTimeout,
            Reasons = [],
            Recommendations = [],
            ApprovalExpiresAt = new DateTimeOffset(2026, 11, 18, 2, 45, 0, TimeSpan.Zero),
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
        var json = V2Samples.MutateResult(root =>
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
        var json = V2Samples.MutateResult(root =>
        {
            switch (caseName)
            {
                case "approved-with-expiry": root["approvalExpiresAt"] = "2026-11-18T02:45:00Z"; break;
                case "rejected-with-expiry": root["status"] = "REJECTED"; root["deposit"] = null; root["approvalExpiresAt"] = "2026-11-18T02:45:00Z"; break;
                case "deposit-rate-above-one": V2Samples.Object(root["deposit"])["rate"] = 1.5; break;
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
        var results = ContractSchemas.Evaluate(RequestSchema, V2Samples.RequestJson, requireFormatValidation: false);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }
}
