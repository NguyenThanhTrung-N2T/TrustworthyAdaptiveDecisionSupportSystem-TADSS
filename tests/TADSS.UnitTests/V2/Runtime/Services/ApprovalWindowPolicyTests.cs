using FluentAssertions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.Runtime.Api.V2.Configuration;
using TADSS.Runtime.Api.V2.Services.Decisions;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Services;

/// <summary>
/// UT-10: approval window (<see cref="ApprovalWindowPolicy"/>, INTEGRATION_SPEC §1.1) — <c>APPROVED</c>/<c>REJECTED</c>
/// giữ nguyên; <c>NEED_APPROVAL</c> có hạn <c>min(now + WindowMinutes, earliestStart − MinLeadMinutes)</c>, và khi hạn
/// không còn ở tương lai thì chuyển thành <c>APPROVED</c> cọc 100%. Thời điểm là tham số, không đọc đồng hồ.
/// </summary>
public class ApprovalWindowPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    private const string RejectedCode = "TEST_POLICY_REJECTED";

    private static readonly string[] StrategyReasons = ["Đơn giá trị lớn cần Manager duyệt."];

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_Approved_PassesThrough()
    {
        var decision = StrategyDecision.Approve(0.3m, ReasonCodes.TierDeposit, ["Cọc theo hạng."]);

        var result = CreatePolicy().Apply(decision, Now.AddDays(3), Now);

        result.Status.Should().Be(DecisionStatus.APPROVED);
        result.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        result.DepositRate.Should().Be(0.3m);
        result.ReasonCode.Should().Be(ReasonCodes.TierDeposit);
        result.Reasons.Should().Equal("Cọc theo hạng.");
        result.Recommendations.Should().BeEmpty();
        result.ApprovalExpiresAt.Should().BeNull();
        result.WindowUnavailable.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_ApprovedEvenWhenStartInPast_PassesThrough()
    {
        var decision = StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng."]);

        var result = CreatePolicy().Apply(decision, Now.AddHours(-1), Now);

        result.Status.Should().Be(DecisionStatus.APPROVED);
        result.DepositRate.Should().Be(0.5m);
        result.WindowUnavailable.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_Rejected_PassesThrough()
    {
        var decision = StrategyDecision.Reject(RejectedCode, ["Bị từ chối theo chính sách."]);

        var result = CreatePolicy().Apply(decision, Now.AddDays(3), Now);

        result.Status.Should().Be(DecisionStatus.REJECTED);
        result.StrategyStatus.Should().Be(DecisionStatus.REJECTED);
        result.DepositRate.Should().BeNull();
        result.ReasonCode.Should().Be(RejectedCode);
        result.Reasons.Should().Equal("Bị từ chối theo chính sách.");
        result.ApprovalExpiresAt.Should().BeNull();
        result.WindowUnavailable.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_NeedApprovalFarFuture_UsesWindowMinutes()
    {
        var decision = NeedApproval();

        var result = CreatePolicy().Apply(decision, Now.AddDays(3), Now);

        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        result.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
        result.ApprovalExpiresAt.Should().Be(Now.AddMinutes(30));
        result.DepositRate.Should().BeNull();
        result.ReasonCode.Should().Be(decision.ReasonCode);
        result.Reasons.Should().Equal(StrategyReasons);
        result.Recommendations.Should().Equal(decision.Recommendations);
        result.WindowUnavailable.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_NeedApprovalCloseStart_UsesLeadTime()
    {
        // start − 120 phút = now + 20 phút, sớm hơn now + 30 phút
        var result = CreatePolicy().Apply(NeedApproval(), Now.AddMinutes(140), Now);

        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        result.ApprovalExpiresAt.Should().Be(Now.AddMinutes(20));
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_ExpiryEqualsNow_BecomesApprovedFullDeposit()
    {
        var result = CreatePolicy().Apply(NeedApproval(), Now.AddMinutes(120), Now);

        AssertWindowUnavailable(result);
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_ExpiryOneMinuteAfterNow_StaysNeedApproval()
    {
        var result = CreatePolicy().Apply(NeedApproval(), Now.AddMinutes(121), Now);

        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        result.ApprovalExpiresAt.Should().Be(Now.AddMinutes(1));
        result.WindowUnavailable.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_StartInPast_BecomesApprovedFullDeposit()
    {
        var result = CreatePolicy().Apply(NeedApproval(), Now.AddHours(-5), Now);

        AssertWindowUnavailable(result);
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_NonUtcInputs_ReturnsUtcExpiry()
    {
        var vietnam = TimeSpan.FromHours(7);
        var nowLocal = Now.ToOffset(vietnam);
        var farStart = Now.AddDays(3).ToOffset(vietnam);
        var closeStart = Now.AddMinutes(140).ToOffset(vietnam);

        var windowBound = CreatePolicy().Apply(NeedApproval(), farStart, nowLocal);
        var leadBound = CreatePolicy().Apply(NeedApproval(), closeStart, nowLocal);

        windowBound.ApprovalExpiresAt.Should().Be(Now.AddMinutes(30));
        leadBound.ApprovalExpiresAt.Should().Be(Now.AddMinutes(20));
        windowBound.ApprovalExpiresAt.Should().NotBeNull();
        leadBound.ApprovalExpiresAt.Should().NotBeNull();
        windowBound.ApprovalExpiresAt.GetValueOrDefault().Offset.Should().Be(TimeSpan.Zero);
        leadBound.ApprovalExpiresAt.GetValueOrDefault().Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_CustomOptions_Respected()
    {
        var policy = CreatePolicy(windowMinutes: 10, minLeadMinutes: 60);

        var windowBound = policy.Apply(NeedApproval(), Now.AddDays(1), Now);
        var leadBound = policy.Apply(NeedApproval(), Now.AddMinutes(65), Now);
        var unavailable = policy.Apply(NeedApproval(), Now.AddMinutes(60), Now);

        windowBound.ApprovalExpiresAt.Should().Be(Now.AddMinutes(10));
        leadBound.ApprovalExpiresAt.Should().Be(Now.AddMinutes(5));
        AssertWindowUnavailable(unavailable);
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Apply_ZeroLeadMinutes_AllowsWindowUpToStart()
    {
        var policy = CreatePolicy(windowMinutes: 30, minLeadMinutes: 0);

        var closeStart = policy.Apply(NeedApproval(), Now.AddMinutes(10), Now);
        var atNow = policy.Apply(NeedApproval(), Now, Now);

        closeStart.ApprovalExpiresAt.Should().Be(Now.AddMinutes(10));
        AssertWindowUnavailable(atNow);
    }

    public static TheoryData<int, int> InvariantCases => new()
    {
        { 0, 3 * 24 * 60 },  // APPROVED
        { 1, 3 * 24 * 60 },  // REJECTED
        { 2, 3 * 24 * 60 },  // NEED_APPROVAL, hạn theo WindowMinutes
        { 2, 140 },          // NEED_APPROVAL, hạn theo MinLeadMinutes
        { 2, 121 },          // NEED_APPROVAL, hạn sát now
        { 2, 120 },          // NEED_APPROVAL → APPROVED 100% (biên)
        { 2, -300 },         // NEED_APPROVAL → APPROVED 100% (giờ bắt đầu đã qua)
    };

    [Theory]
    [Trait("CatalogId", "UT-10")]
    [MemberData(nameof(InvariantCases))]
    public void Apply_AllPaths_SatisfyInv7_Theory(int kind, int startOffsetMinutes)
    {
        var decision = kind switch
        {
            0 => StrategyDecision.Approve(0.3m, ReasonCodes.TierDeposit, ["Cọc theo hạng."]),
            1 => StrategyDecision.Reject(RejectedCode, ["Bị từ chối theo chính sách."]),
            _ => NeedApproval(),
        };

        var result = CreatePolicy().Apply(decision, Now.AddMinutes(startOffsetMinutes), Now);

        (result.Status == DecisionStatus.APPROVED).Should().Be(result.DepositRate is not null, "INV-7: APPROVED ⇔ có mức cọc");
        (result.Status == DecisionStatus.NEED_APPROVAL).Should().Be(result.ApprovalExpiresAt is not null, "INV-7: NEED_APPROVAL ⇔ có hạn duyệt");
        if (result.ApprovalExpiresAt is { } expires)
        {
            expires.Should().BeAfter(Now, "hạn duyệt luôn ở tương lai");
        }
    }

    private static void AssertWindowUnavailable(ApprovalWindowResult result)
    {
        result.Status.Should().Be(DecisionStatus.APPROVED);
        result.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
        result.DepositRate.Should().Be(1m);
        result.ReasonCode.Should().Be(ReasonCodes.ApprovalWindowUnavailable);
        result.Reasons.Should().Equal("Không còn đủ thời gian để Manager duyệt trước giờ bắt đầu, nên áp dụng mức cọc 100% thay cho bước duyệt.");
        result.Recommendations.Should().BeEmpty();
        result.ApprovalExpiresAt.Should().BeNull();
        result.WindowUnavailable.Should().BeTrue();
    }

    private static StrategyDecision NeedApproval() =>
        StrategyDecision.RequireApproval(
            ReasonCodes.HighValueReview,
            StrategyReasons,
            [new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Cần Manager duyệt.")]);

    private static ApprovalWindowPolicy CreatePolicy(int windowMinutes = 30, int minLeadMinutes = 120) =>
        new(Options.Create(new ApprovalOptions { WindowMinutes = windowMinutes, MinLeadMinutes = minLeadMinutes }));
}
