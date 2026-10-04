using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Infrastructure.Resilience;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// UT-07 (phần fallback): <see cref="FallbackDecisionPolicy"/> tạo quyết định an toàn cho từng nguyên nhân —
/// luôn <c>APPROVED</c> với tỷ lệ cọc 100%, đúng <c>reasonCode</c> và không kèm khuyến nghị (D-13: mọi sự cố kỹ thuật
/// đều cọc 100%, <c>NEED_APPROVAL</c> chỉ dành cho bất định nghiệp vụ do strategy quyết định).
/// </summary>
public class FallbackDecisionPolicyTests
{
    [Theory]
    [Trait("CatalogId", "UT-07")]
    [InlineData(FallbackCause.Timeout, ReasonCodes.FallbackStrategyTimeout)]
    [InlineData(FallbackCause.Exception, ReasonCodes.FallbackStrategyException)]
    [InlineData(FallbackCause.CircuitOpen, ReasonCodes.FallbackCircuitOpen)]
    public void Create_EachCause_ApprovedFullDepositWithMatchingCode_Theory(FallbackCause cause, string expectedReasonCode)
    {
        var decision = FallbackDecisionPolicy.Create(cause);

        decision.Status.Should().Be(DecisionStatus.APPROVED);
        decision.DepositRate.Should().Be(1m);
        decision.ReasonCode.Should().Be(expectedReasonCode);
        decision.Reasons.Should().HaveCount(2).And.OnlyContain(reason => !string.IsNullOrWhiteSpace(reason));
        decision.Reasons[0].Should().StartWith("Chế độ an toàn tự động kích hoạt: ");
        decision.Reasons[1].Should().Contain("100%");
        decision.Recommendations.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void FullDepositRate_IsOne()
    {
        FallbackDecisionPolicy.FullDepositRate.Should().Be(1m);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Create_UndefinedCause_ThrowsArgumentOutOfRange()
    {
        var act = () => FallbackDecisionPolicy.Create((FallbackCause)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
