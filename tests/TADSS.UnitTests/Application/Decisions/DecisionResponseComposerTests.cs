using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Decisions;
using TADSS.Application.Options;
using TADSS.Application.Strategies;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-10/UT-11/UT-13, CT-01: <see cref="DecisionResponseComposer"/> ghép quyết định của strategy với approval window và
/// mức cọc thành response v2 cùng bản ghi audit — hai bên cùng dữ liệu nghiệp vụ, INV-7 luôn đúng, thời gian lấy từ một
/// <c>NowUtc</c> duy nhất (B-03), strategy giữ danh tính của strategy đã chọn khi fallback (B-04), payload serialize bằng
/// <see cref="TadssJson"/> và response hợp lệ theo <c>decision-result.schema.json</c>.
/// </summary>
public class DecisionResponseComposerTests
{
    private const string ResultSchema = "decision-result.schema.json";
    private const string IdempotencyKey = "idem-key-0001";
    private const string CorrelationId = "corr-0001";
    private const string RequestHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    public static TheoryData<string> AllScenarios => new()
    {
        Scenario.ApprovedAutomated,
        Scenario.NeedApproval,
        Scenario.NeedApprovalNoWindow,
        Scenario.Rejected,
        Scenario.Fallback,
        Scenario.CacheHit
    };

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Compose_ApprovedAutomated_HasDepositAndNoExpiry()
    {
        var composed = CreateComposer().Compose(CreateComposition(Scenario.ApprovedAutomated));

        var response = composed.Response;
        var deposit = ContractSamples.Required(response.Deposit);
        response.Status.Should().Be(DecisionStatus.APPROVED);
        deposit.Rate.Should().Be(0.5m);
        deposit.Amount.Should().Be(114_000m);
        deposit.Currency.Should().Be("VND");
        response.ApprovalExpiresAt.Should().BeNull();
        response.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        response.FallbackUsed.Should().BeFalse();
        response.ReasonCode.Should().Be(ReasonCodes.PeakHourDeposit);
        response.Reasons.Should().Equal("Khung giờ cao điểm áp dụng cọc 50%.");

        var record = composed.Record;
        record.Status.Should().Be(DecisionStatus.APPROVED);
        record.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        record.DepositRate.Should().Be(0.5m);
        record.DepositAmount.Should().Be(114_000m);
        record.ApprovalExpiresAt.Should().BeNull();
        record.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        record.Role.Should().Be(ExecutionRole.PRIMARY);
        record.FallbackUsed.Should().BeFalse();
        record.FailureReason.Should().BeNull();
        record.CacheHit.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Compose_NeedApproval_HasExpiryNoDeposit()
    {
        var composed = CreateComposer().Compose(CreateComposition(Scenario.NeedApproval));

        composed.Response.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        composed.Response.Deposit.Should().BeNull();
        composed.Response.ApprovalExpiresAt.Should().Be(Now.AddMinutes(30));
        composed.Response.ReasonCode.Should().Be(ReasonCodes.HighValueReview);
        composed.Response.Recommendations.Should().ContainSingle()
            .Which.Type.Should().Be(RecommendationType.REQUIRE_APPROVAL);

        composed.Record.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        composed.Record.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
        composed.Record.DepositRate.Should().BeNull();
        composed.Record.DepositAmount.Should().BeNull();
        composed.Record.ApprovalExpiresAt.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Compose_NeedApprovalNoWindow_ApprovedFullDeposit()
    {
        var composed = CreateComposer().Compose(CreateComposition(Scenario.NeedApprovalNoWindow));

        var deposit = ContractSamples.Required(composed.Response.Deposit);
        composed.Response.Status.Should().Be(DecisionStatus.APPROVED);
        deposit.Rate.Should().Be(1m);
        deposit.Amount.Should().Be(228_000m);
        composed.Response.ReasonCode.Should().Be(ReasonCodes.ApprovalWindowUnavailable);
        composed.Response.Recommendations.Should().BeEmpty();
        composed.Response.ApprovalExpiresAt.Should().BeNull();

        composed.Record.Status.Should().Be(DecisionStatus.APPROVED);
        composed.Record.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
        composed.Record.DepositRate.Should().Be(1m);
        composed.Record.DepositAmount.Should().Be(228_000m);
        composed.Record.ReasonCode.Should().Be(ReasonCodes.ApprovalWindowUnavailable);
        composed.Record.ApprovalExpiresAt.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Compose_Rejected_NoDepositNoExpiry()
    {
        var composed = CreateComposer().Compose(CreateComposition(Scenario.Rejected));

        composed.Response.Status.Should().Be(DecisionStatus.REJECTED);
        composed.Response.Deposit.Should().BeNull();
        composed.Response.ApprovalExpiresAt.Should().BeNull();

        composed.Record.Status.Should().Be(DecisionStatus.REJECTED);
        composed.Record.StrategyStatus.Should().Be(DecisionStatus.REJECTED);
        composed.Record.DepositRate.Should().BeNull();
        composed.Record.DepositAmount.Should().BeNull();
        composed.Record.ApprovalExpiresAt.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_Fallback_KeepsSelectedStrategyIdentity()
    {
        var composition = CreateComposition(Scenario.Fallback);

        var composed = CreateComposer().Compose(composition);

        composed.Response.StrategyName.Should().Be(composition.Strategy.Strategy.StrategyName);
        composed.Response.StrategyVersion.Should().Be(composition.Strategy.Strategy.StrategyVersion);
        composed.Response.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        composed.Response.FallbackUsed.Should().BeTrue();
        composed.Response.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyTimeout);
        composed.Response.Status.Should().Be(DecisionStatus.APPROVED);
        var deposit = ContractSamples.Required(composed.Response.Deposit);
        deposit.Rate.Should().Be(1m);
        deposit.Amount.Should().Be(composition.Reservation.TotalPrice);
        composed.Response.ApprovalExpiresAt.Should().BeNull();

        composed.Record.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        composed.Record.Status.Should().Be(DecisionStatus.APPROVED);
        composed.Record.DepositRate.Should().Be(1m);
        composed.Record.DepositAmount.Should().Be(composition.Reservation.TotalPrice);
        composed.Record.ApprovalExpiresAt.Should().BeNull();
        composed.Record.StrategyName.Should().Be(composition.Strategy.Strategy.StrategyName);
        composed.Record.StrategyVersion.Should().Be(composition.Strategy.Strategy.StrategyVersion);
        composed.Record.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        composed.Record.FallbackUsed.Should().BeTrue();
        composed.Record.FailureReason.Should().Be(composition.Outcome.FailureReason);
        composed.Record.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyTimeout);
        composed.Record.ExecutionTimeMs.Should().Be(composition.Outcome.ExecutionTimeMs);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Compose_CacheHit_MarksCacheHit()
    {
        var composed = CreateComposer().Compose(CreateComposition(Scenario.CacheHit));

        composed.Response.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        composed.Response.FallbackUsed.Should().BeFalse();

        composed.Record.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        composed.Record.CacheHit.Should().BeTrue();
        composed.Record.FallbackUsed.Should().BeFalse();
        composed.Record.FailureReason.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Compose_CacheHitWithFallbackOutcome_ClearsFallbackFields()
    {
        // Dù outcome mang dấu hiệu fallback, CACHE_HIT thắng: kết quả lấy từ cache không phải fallback
        var composition = CreateComposition(Scenario.CacheHit) with { Outcome = CreateOutcome(Scenario.Fallback) };

        var composed = CreateComposer().Compose(composition);

        composed.Response.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        composed.Response.FallbackUsed.Should().BeFalse();
        composed.Record.CacheHit.Should().BeTrue();
        composed.Record.FallbackUsed.Should().BeFalse();
        composed.Record.FailureReason.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Compose_ApprovedWithRateScaleOverFour_Throws()
    {
        var composition = CreateComposition(Scenario.ApprovedAutomated) with
        {
            Outcome = new StrategyExecutionOutcome(
                StrategyDecision.Approve(0.12345m, ReasonCodes.PeakHourDeposit, ["Tỷ lệ có quá nhiều chữ số thập phân."]),
                ExecutionType.AUTOMATED, false, null, 12)
        };

        var act = () => CreateComposer().Compose(composition);

        act.Should().Throw<InvalidOperationException>().WithMessage("*4 chữ số thập phân*");
    }

    [Theory]
    [Trait("CatalogId", "UT-10")]
    [InlineData("0.1234")]
    [InlineData("0.5000")]
    [InlineData("0.50000")]
    [InlineData("1")]
    public void Compose_ApprovedWithRateUpToFourDecimals_Succeeds_Theory(string rateText)
    {
        var rate = decimal.Parse(rateText, System.Globalization.CultureInfo.InvariantCulture);
        var composition = CreateComposition(Scenario.ApprovedAutomated) with
        {
            Outcome = new StrategyExecutionOutcome(
                StrategyDecision.Approve(rate, ReasonCodes.PeakHourDeposit, ["Tỷ lệ hợp lệ."]),
                ExecutionType.AUTOMATED, false, null, 12)
        };

        var composed = CreateComposer().Compose(composition);

        composed.Response.Status.Should().Be(DecisionStatus.APPROVED);
        ContractSamples.Required(composed.Response.Deposit).Rate.Should().Be(rate);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_Timestamps_UseNowUtc()
    {
        // NowUtc có offset khác 0: mọi mốc thời gian phải quy về offset 0 và cùng một instant
        var nowWithOffset = new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7));
        var composition = CreateComposition(Scenario.ApprovedAutomated) with { NowUtc = nowWithOffset };

        var composed = CreateComposer().Compose(composition);

        composed.Response.EvaluatedAt.Should().Be(nowWithOffset);
        composed.Response.EvaluatedAt.Offset.Should().Be(TimeSpan.Zero);
        composed.Record.CreatedAt.Should().Be(nowWithOffset);
        composed.Record.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        composed.Record.CreatedAt.Should().Be(composed.Response.EvaluatedAt);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_NeedApprovalWithOffsetNow_ExpiryHasZeroOffset()
    {
        var nowWithOffset = new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7));
        var composition = CreateComposition(Scenario.NeedApproval) with { NowUtc = nowWithOffset };

        var composed = CreateComposer().Compose(composition);

        composed.Response.ApprovalExpiresAt.Should().Be(nowWithOffset.AddMinutes(30));
        (composed.Response.ApprovalExpiresAt?.Offset).Should().Be(TimeSpan.Zero);
        composed.Record.ApprovalExpiresAt.Should().Be(nowWithOffset.AddMinutes(30));
        (composed.Record.ApprovalExpiresAt?.Offset).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_Identity_IsCopiedToResponseAndRecord()
    {
        var composition = CreateComposition(Scenario.ApprovedAutomated);

        var composed = CreateComposer().Compose(composition);

        composed.Response.DecisionId.Should().Be(composition.DecisionId);
        composed.Response.ReservationId.Should().Be(composition.Reservation.ReservationId);
        composed.Record.DecisionId.Should().Be(composition.DecisionId);
        composed.Record.ReservationId.Should().Be(composition.Reservation.ReservationId);
        composed.Record.BranchId.Should().Be(composition.Reservation.BranchId);
        composed.Record.RequestHash.Should().Be(RequestHash);
        composed.Record.IdempotencyKey.Should().Be(IdempotencyKey);
        composed.Record.CorrelationId.Should().Be(CorrelationId);
        composed.Record.ExecutionTimeMs.Should().Be(composition.Outcome.ExecutionTimeMs);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_NullCorrelationId_IsKeptNull()
    {
        var composition = CreateComposition(Scenario.ApprovedAutomated) with { CorrelationId = null };

        var composed = CreateComposer().Compose(composition);

        composed.Record.CorrelationId.Should().BeNull();
        composed.Record.IdempotencyKey.Should().Be(IdempotencyKey);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_Payloads_AreTadssJsonOfInputs()
    {
        var composition = CreateComposition(Scenario.ApprovedAutomated);

        var composed = CreateComposer().Compose(composition);

        // Request: serialize đúng bằng TadssJson, gồm cả demand, metadata và requestedAt
        composed.Record.RequestPayload.Should().Be(JsonSerializer.Serialize(composition.Request, TadssJson.Options));
        var requestNode = ContractSamples.Object(JsonNode.Parse(composed.Record.RequestPayload));
        requestNode.Should().ContainKey("demand");
        requestNode.Should().ContainKey("metadata");
        requestNode.Should().ContainKey("requestedAt");

        var requestBack = ContractSamples.Required(
            JsonSerializer.Deserialize<DecisionRequestDto>(composed.Record.RequestPayload, TadssJson.Options));
        requestBack.Should().BeEquivalentTo(composition.Request);
        ContractSamples.Required(requestBack.Demand).OccupancyRate.Should().Be(0.75m);
        ContractSamples.Required(requestBack.Metadata).Should().ContainKey("promotionCode");

        // Response: payload là chính response, serialize bằng TadssJson
        composed.Record.ResponsePayload.Should().Be(JsonSerializer.Serialize(composed.Response, TadssJson.Options));
        var responseBack = ContractSamples.Required(
            JsonSerializer.Deserialize<DecisionResultDto>(composed.Record.ResponsePayload, TadssJson.Options));
        responseBack.Should().BeEquivalentTo(composed.Response);
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [MemberData(nameof(AllScenarios))]
    public void Compose_ResponsePayload_ValidAgainstResultSchema_Theory(string scenario)
    {
        var composed = CreateComposer().Compose(CreateComposition(scenario));

        var results = ContractSchemas.Evaluate(ResultSchema, composed.Record.ResponsePayload);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Theory]
    [Trait("CatalogId", "UT-10")]
    [MemberData(nameof(AllScenarios))]
    public void Compose_AllPaths_SatisfyInv7_Theory(string scenario)
    {
        var composed = CreateComposer().Compose(CreateComposition(scenario));

        var response = composed.Response;
        (response.Status == DecisionStatus.APPROVED).Should().Be(response.Deposit is not null, "INV-7: APPROVED ⇔ có mức cọc (response)");
        (response.Status == DecisionStatus.NEED_APPROVAL).Should().Be(response.ApprovalExpiresAt is not null, "INV-7: NEED_APPROVAL ⇔ có hạn duyệt (response)");

        var record = composed.Record;
        (record.Status == DecisionStatus.APPROVED).Should().Be(record.DepositRate is not null && record.DepositAmount is not null, "INV-7: APPROVED ⇔ có mức cọc (record)");
        (record.DepositRate is null).Should().Be(record.DepositAmount is null, "mức cọc và số tiền cọc luôn đi cùng nhau");
        (record.Status == DecisionStatus.NEED_APPROVAL).Should().Be(record.ApprovalExpiresAt is not null, "INV-7: NEED_APPROVAL ⇔ có hạn duyệt (record)");

        record.Status.Should().Be(response.Status);
        record.ReasonCode.Should().Be(response.ReasonCode);
        record.StrategyName.Should().Be(response.StrategyName);
        record.StrategyVersion.Should().Be(response.StrategyVersion);
        record.ExecutionType.Should().Be(response.ExecutionType);
        record.FallbackUsed.Should().Be(response.FallbackUsed);
        record.DepositRate.Should().Be(response.Deposit?.Rate);
        record.DepositAmount.Should().Be(response.Deposit?.Amount);
        record.ApprovalExpiresAt.Should().Be(response.ApprovalExpiresAt);
    }

    private static DecisionResponseComposer CreateComposer(int windowMinutes = 30, int minLeadMinutes = 120) =>
        new(
            new DepositCalculator(),
            new ApprovalWindowPolicy(MsOptions.Create(new ApprovalOptions { WindowMinutes = windowMinutes, MinLeadMinutes = minLeadMinutes })));

    private static StrategyExecutionOutcome CreateOutcome(string scenario) => scenario switch
    {
        Scenario.ApprovedAutomated or Scenario.CacheHit => new StrategyExecutionOutcome(
            StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Khung giờ cao điểm áp dụng cọc 50%."]),
            ExecutionType.AUTOMATED, false, null, 12),
        Scenario.NeedApproval or Scenario.NeedApprovalNoWindow => new StrategyExecutionOutcome(
            StrategyDecision.RequireApproval(
                ReasonCodes.HighValueReview,
                ["Đơn giá trị lớn cần Manager duyệt."],
                [new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Cần Manager duyệt.")]),
            ExecutionType.AUTOMATED, false, null, 7),
        Scenario.Rejected => new StrategyExecutionOutcome(
            StrategyDecision.Reject("TEST_POLICY_REJECTED", ["Bị từ chối theo chính sách."]),
            ExecutionType.AUTOMATED, false, null, 3),
        Scenario.Fallback => new StrategyExecutionOutcome(
            StrategyDecision.Approve(1m, ReasonCodes.FallbackStrategyTimeout, ["Strategy quá hạn, dùng quyết định dự phòng."]),
            ExecutionType.FALLBACK, true, $"{FailureReasonPrefixes.StrategyTimeout}: vượt 400 ms", 412),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Kịch bản không hợp lệ.")
    };

    private static DecisionComposition CreateComposition(string scenario)
    {
        // NeedApprovalNoWindow: giờ bắt đầu gần hơn MinLead nên hạn duyệt đã ở quá khứ
        var start = scenario == Scenario.NeedApprovalNoWindow ? Now.AddHours(1) : Now.AddDays(3);
        var request = ContractSamples.DeserializeRequest(ContractSamples.RequestJson);
        var reservation = DomainTestData.CreateReservation(
            items: [DomainTestData.CreateItem(start)],
            totalPrice: 228_000m,
            reservationId: request.ReservationId,
            branchId: request.BranchId);
        var strategy = new RegisteredStrategy(
            new DelegateStrategy((_, _, _) => Task.FromResult(StrategyDecision.Reject("UNUSED", ["Không dùng."]))),
            "0123456789abcdef");

        return new DecisionComposition(
            DecisionId: Guid.NewGuid(),
            Request: request,
            Reservation: reservation,
            Strategy: strategy,
            Outcome: CreateOutcome(scenario),
            CacheHit: scenario == Scenario.CacheHit,
            NowUtc: Now,
            RequestHash: RequestHash,
            IdempotencyKey: IdempotencyKey,
            CorrelationId: CorrelationId);
    }

    /// <summary>Bản SHADOW có đúng Role và không có khóa idempotency; trạng thái, cọc và response như bản PRIMARY cùng đầu vào (chỉ <c>decisionId</c> khác).</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Compose_ShadowRole_RecordHasRoleAndNullIdempotencyKey()
    {
        var composer = CreateComposer();
        var primaryInput = CreateComposition(Scenario.ApprovedAutomated);
        var shadowInput = primaryInput with { DecisionId = Guid.NewGuid(), IdempotencyKey = null, Role = ExecutionRole.SHADOW };

        var primary = composer.Compose(primaryInput);
        var shadow = composer.Compose(shadowInput);

        shadow.Record.Role.Should().Be(ExecutionRole.SHADOW);
        shadow.Record.IdempotencyKey.Should().BeNull();
        primary.Record.Role.Should().Be(ExecutionRole.PRIMARY);
        shadow.Record.Should().BeEquivalentTo(
            primary.Record,
            options => options.Excluding(record => record.DecisionId)
                .Excluding(record => record.Role)
                .Excluding(record => record.IdempotencyKey)
                .Excluding(record => record.ResponsePayload));
        shadow.Response.Should().BeEquivalentTo(primary.Response, options => options.Excluding(response => response.DecisionId));
    }

    /// <summary>Bản PRIMARY thiếu khóa idempotency hoặc bản SHADOW mang khóa là lỗi lập trình: ném lỗi thay vì ghi bản ghi lệch (INV-2, INV-8).</summary>
    [Theory]
    [Trait("CatalogId", "UT-11")]
    [InlineData(ExecutionRole.PRIMARY, false)]
    [InlineData(ExecutionRole.SHADOW, true)]
    public void Compose_IdempotencyKeyDoesNotMatchRole_Throws(ExecutionRole role, bool withKey)
    {
        var composition = CreateComposition(Scenario.ApprovedAutomated) with
        {
            Role = role,
            IdempotencyKey = withKey ? IdempotencyKey : null,
        };

        var act = () => CreateComposer().Compose(composition);

        act.Should().Throw<ArgumentException>();
    }

    private static class Scenario
    {
        public const string ApprovedAutomated = nameof(ApprovedAutomated);
        public const string NeedApproval = nameof(NeedApproval);
        public const string NeedApprovalNoWindow = nameof(NeedApprovalNoWindow);
        public const string Rejected = nameof(Rejected);
        public const string Fallback = nameof(Fallback);
        public const string CacheHit = nameof(CacheHit);
    }
}
