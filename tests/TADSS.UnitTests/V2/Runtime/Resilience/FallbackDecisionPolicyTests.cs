using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Resilience;

/// <summary>
/// UT-07 (phần fallback): <see cref="FallbackDecisionPolicy"/> tạo quyết định an toàn của v2 cho từng nguyên nhân —
/// luôn <c>NEED_APPROVAL</c> với đúng <c>reasonCode</c> và một khuyến nghị <c>REQUIRE_APPROVAL</c>; lý do không nhắc
/// "100%" vì mức cọc 100% là chính sách của SmashCourt, không phải của TADSS.
/// </summary>
public class FallbackDecisionPolicyTests
{
    [Theory]
    [Trait("CatalogId", "UT-07")]
    [InlineData(FallbackCause.Timeout, ReasonCodes.FallbackStrategyTimeout)]
    [InlineData(FallbackCause.Exception, ReasonCodes.FallbackStrategyException)]
    [InlineData(FallbackCause.CircuitOpen, ReasonCodes.FallbackCircuitOpen)]
    public void Create_EachCause_RequireApprovalWithMatchingCode_Theory(FallbackCause cause, string expectedReasonCode)
    {
        var decision = FallbackDecisionPolicy.Create(cause);

        decision.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        decision.DepositRate.Should().BeNull();
        decision.ReasonCode.Should().Be(expectedReasonCode);
        decision.Reasons.Should().NotBeEmpty().And.OnlyContain(reason => !string.IsNullOrWhiteSpace(reason));
        decision.Reasons.Should().OnlyContain(reason => !reason.Contains("100%"));
        decision.Recommendations.Should().ContainSingle()
            .Which.Type.Should().Be(RecommendationType.REQUIRE_APPROVAL);
        decision.Recommendations[0].Description.Should().NotContain("100%");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Create_UndefinedCause_ThrowsArgumentOutOfRange()
    {
        var act = () => FallbackDecisionPolicy.Create((FallbackCause)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
