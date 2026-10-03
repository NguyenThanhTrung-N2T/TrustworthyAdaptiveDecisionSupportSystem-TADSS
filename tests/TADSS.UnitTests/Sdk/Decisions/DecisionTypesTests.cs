using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Sdk.Decisions;

/// <summary>
/// UT-01: bất biến của kiểu quyết định v2 — <see cref="StrategyDecision"/> chỉ tạo qua factory, tỷ lệ cọc trong
/// [0, 1], mã lý do 1..64 ký tự; <see cref="DecisionContext"/> ở UTC; <see cref="StrategyCachePolicy"/> TTL dương.
/// </summary>
public class DecisionTypesTests
{
    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Approve_RateWithinBounds_CreatesApprovedDecision(double rate)
    {
        var decision = StrategyDecision.Approve((decimal)rate, "TIER_DEPOSIT", ["lý do"]);

        decision.Status.Should().Be(DecisionStatus.APPROVED);
        decision.DepositRate.Should().Be((decimal)rate);
        decision.Recommendations.Should().BeEmpty();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Approve_RateOutOfBounds_ThrowsInvalidDepositRate(double rate)
    {
        var act = () => StrategyDecision.Approve((decimal)rate, "TIER_DEPOSIT", []);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_DEPOSIT_RATE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void RequireApproval_Inputs_AreCopiedAndHaveNoDepositRate()
    {
        var reasons = new List<string> { "giá trị lớn" };
        var recommendations = new List<DecisionRecommendation> { new(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét.") };

        var decision = StrategyDecision.RequireApproval("HIGH_VALUE_REVIEW", reasons, recommendations);
        reasons.Add("thêm sau");
        recommendations.Clear();

        decision.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        decision.DepositRate.Should().BeNull();
        decision.Reasons.Should().ContainSingle();
        decision.Recommendations.Should().ContainSingle();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Reject_CreatesRejectedDecisionWithoutDepositRate()
    {
        var decision = StrategyDecision.Reject("POLICY_REJECT", ["không phục vụ"]);

        decision.Status.Should().Be(DecisionStatus.REJECTED);
        decision.DepositRate.Should().BeNull();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(DecisionStatus.APPROVED)]
    [InlineData(DecisionStatus.NEED_APPROVAL)]
    [InlineData(DecisionStatus.REJECTED)]
    public void Factory_NullReasonElement_ThrowsInvalidReasons(DecisionStatus status)
    {
        // Cast không kiểm tra null nên mảng kết quả giữ phần tử null mà không cần toán tử '!'
        var reasons = new string?[] { "lý do", null }.Cast<string>().ToArray();

        var act = () => Create(status, reasons);

        var exception = act.Should().Throw<DomainInvariantException>().Which;
        exception.InvariantCode.Should().Be("INVALID_REASONS");
        exception.FieldName.Should().Be(nameof(StrategyDecision.Reasons));
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void RequireApproval_NullRecommendationElement_ThrowsInvalidRecommendations()
    {
        var recommendations = new DecisionRecommendation?[] { new(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét."), null }
            .Cast<DecisionRecommendation>()
            .ToArray();

        var act = () => StrategyDecision.RequireApproval("HIGH_VALUE_REVIEW", ["lý do"], recommendations);

        var exception = act.Should().Throw<DomainInvariantException>().Which;
        exception.InvariantCode.Should().Be("INVALID_RECOMMENDATIONS");
        exception.FieldName.Should().Be(nameof(StrategyDecision.Recommendations));
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(DecisionStatus.APPROVED)]
    [InlineData(DecisionStatus.NEED_APPROVAL)]
    [InlineData(DecisionStatus.REJECTED)]
    public void Reasons_AnyDecision_IsNotWritableArray(DecisionStatus status)
    {
        var decision = Create(status, ["lý do"]);

        decision.Reasons.Should().NotBeAssignableTo<string[]>("downcast sang mảng sẽ cho phép ghi đè nội dung bất biến");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Recommendations_RequireApproval_IsNotWritableArray()
    {
        var decision = StrategyDecision.RequireApproval(
            "HIGH_VALUE_REVIEW", ["lý do"], [new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét.")]);

        decision.Recommendations.Should().NotBeAssignableTo<DecisionRecommendation[]>("downcast sang mảng sẽ cho phép ghi đè nội dung bất biến");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0)]
    [InlineData(65)]
    public void Factory_ReasonCodeLengthOutOfRange_ThrowsInvalidReasonCode(int length)
    {
        var act = () => StrategyDecision.Reject(new string('A', length), []);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_REASON_CODE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Factory_WhitespaceReasonCode_ThrowsInvalidReasonCode()
    {
        var act = () => StrategyDecision.RequireApproval("   ", []);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_REASON_CODE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Factory_ReasonCodeAtMaxLength_IsAllowed()
    {
        var decision = StrategyDecision.Reject(new string('A', StrategyDecision.MaxReasonCodeLength), []);

        decision.ReasonCode.Should().HaveLength(64);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Recommendation_EmptyDescription_ThrowsInvariant()
    {
        var act = () => new DecisionRecommendation(RecommendationType.ALTERNATIVE_SLOT, " ");

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_RECOMMENDATION_DESCRIPTION");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Recommendation_UndefinedType_ThrowsInvariant()
    {
        var act = () => new DecisionRecommendation((RecommendationType)42, "mô tả");

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_RECOMMENDATION_TYPE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Context_NonUtcInstant_ThrowsArgumentException()
    {
        var act = () => new DecisionContext(DomainTestData.FridayPeakStart, DomainTestData.StandardCalendar());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Context_UtcInstant_ExposesValues()
    {
        var context = DomainTestData.CreateContext();

        context.EvaluatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        context.Calendar.PeakWindows.Should().HaveCount(7);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void CachePolicy_Disabled_HasNoTtl()
    {
        StrategyCachePolicy.Disabled.IsEnabled.Should().BeFalse();
        StrategyCachePolicy.Disabled.Ttl.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void CachePolicy_EnabledWithPositiveTtl_ExposesTtl()
    {
        var policy = StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10));

        policy.IsEnabled.Should().BeTrue();
        policy.Ttl.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0)]
    [InlineData(-1)]
    public void CachePolicy_EnabledWithNonPositiveTtl_ThrowsArgumentOutOfRange(int minutes)
    {
        var act = () => StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(minutes));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static StrategyDecision Create(DecisionStatus status, IReadOnlyList<string> reasons) => status switch
    {
        DecisionStatus.APPROVED => StrategyDecision.Approve(0.5m, "TIER_DEPOSIT", reasons),
        DecisionStatus.NEED_APPROVAL => StrategyDecision.RequireApproval("HIGH_VALUE_REVIEW", reasons),
        DecisionStatus.REJECTED => StrategyDecision.Reject("POLICY_REJECT", reasons),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
