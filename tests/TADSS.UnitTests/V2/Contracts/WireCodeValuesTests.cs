using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

/// <summary>
/// CT-02: ghim giá trị wire của <see cref="ErrorCodes"/> (INTEGRATION_SPEC §3.8, 13 mã) và <see cref="ReasonCodes"/>
/// (SAS 4.3 + INTEGRATION_SPEC §1.1, 9 mã). Đếm hằng bằng reflection nên thêm hằng mới mà quên cập nhật bảng
/// kỳ vọng sẽ làm test đỏ.
/// </summary>
public class WireCodeValuesTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedErrorCodes = new Dictionary<string, string>
    {
        [nameof(ErrorCodes.ValidationError)] = "VALIDATION_ERROR",
        [nameof(ErrorCodes.DomainInvariantViolation)] = "DOMAIN_INVARIANT_VIOLATION",
        [nameof(ErrorCodes.MissingHeader)] = "MISSING_HEADER",
        [nameof(ErrorCodes.Unauthorized)] = "UNAUTHORIZED",
        [nameof(ErrorCodes.DecisionNotFound)] = "DECISION_NOT_FOUND",
        [nameof(ErrorCodes.IdempotencyKeyReuse)] = "IDEMPOTENCY_KEY_REUSE",
        [nameof(ErrorCodes.NotPendingApproval)] = "NOT_PENDING_APPROVAL",
        [nameof(ErrorCodes.AlreadyOverridden)] = "ALREADY_OVERRIDDEN",
        [nameof(ErrorCodes.ApprovalExpired)] = "APPROVAL_EXPIRED",
        [nameof(ErrorCodes.OutcomeAlreadyRecorded)] = "OUTCOME_ALREADY_RECORDED",
        [nameof(ErrorCodes.PersistenceUnavailable)] = "PERSISTENCE_UNAVAILABLE",
        [nameof(ErrorCodes.GatewayTimeout)] = "GATEWAY_TIMEOUT",
        [nameof(ErrorCodes.InternalError)] = "INTERNAL_ERROR",
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedReasonCodes = new Dictionary<string, string>
    {
        [nameof(ReasonCodes.TierDeposit)] = "TIER_DEPOSIT",
        [nameof(ReasonCodes.HighValueReview)] = "HIGH_VALUE_REVIEW",
        [nameof(ReasonCodes.NoShowHistoryReview)] = "NO_SHOW_HISTORY_REVIEW",
        [nameof(ReasonCodes.PeakHourDeposit)] = "PEAK_HOUR_DEPOSIT",
        [nameof(ReasonCodes.OffPeakDeposit)] = "OFF_PEAK_DEPOSIT",
        [nameof(ReasonCodes.ApprovalWindowUnavailable)] = "APPROVAL_WINDOW_UNAVAILABLE",
        [nameof(ReasonCodes.FallbackStrategyTimeout)] = "FALLBACK_STRATEGY_TIMEOUT",
        [nameof(ReasonCodes.FallbackStrategyException)] = "FALLBACK_STRATEGY_EXCEPTION",
        [nameof(ReasonCodes.FallbackCircuitOpen)] = "FALLBACK_CIRCUIT_OPEN",
    };

    private static Dictionary<string, string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)(field.GetRawConstantValue() ?? string.Empty));

    [Fact]
    [Trait("CatalogId", "CT-02")]
    public void ErrorCodes_Values_MatchIntegrationSpec()
    {
        var actual = ConstantsOf(typeof(ErrorCodes));

        actual.Should().HaveCount(13, "INTEGRATION_SPEC §3.8 định nghĩa đúng 13 mã lỗi");
        actual.Should().BeEquivalentTo(ExpectedErrorCodes);
    }

    [Fact]
    [Trait("CatalogId", "CT-02")]
    public void ReasonCodes_Values_MatchSpec()
    {
        var actual = ConstantsOf(typeof(ReasonCodes));

        actual.Should().HaveCount(9, "SAS 4.3 và INTEGRATION_SPEC §1.1 định nghĩa đúng 9 mã lý do");
        actual.Should().BeEquivalentTo(ExpectedReasonCodes);
        actual.Values.Should().OnlyContain(value => value.Length <= 64, "reasonCode tối đa 64 ký tự");
    }
}
