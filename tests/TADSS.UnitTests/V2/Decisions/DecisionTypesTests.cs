using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Decisions;

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
        var act = () => new DecisionContext(V2TestData.FridayPeakStart, V2TestData.StandardCalendar());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Context_UtcInstant_ExposesValues()
    {
        var context = V2TestData.CreateContext();

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
}
