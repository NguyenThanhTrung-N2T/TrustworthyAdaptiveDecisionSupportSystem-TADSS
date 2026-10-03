using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Exceptions;
using TADSS.Application.Outcomes;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Outcomes;

/// <summary>
/// UT-15: <see cref="OutcomeService"/> theo F-05: ghi kết cục cho mọi quyết định <c>PRIMARY</c> bất kể trạng thái (<c>SHADOW</c>/không có → 404),
/// chống trùng theo payload (<c>outcome</c> và <c>occurredAt</c> sau khi cắt µs): trùng → trả bản đã lưu, khác → <c>OUTCOME_ALREADY_RECORDED</c>,
/// thua tranh chấp xử lý theo payload của bản thắng, thời điểm trong response là UTC, log không chứa dữ liệu thừa.
/// </summary>
public class OutcomeServiceTests
{
    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");
    private static readonly DateTimeOffset Now = GovernanceTestData.Now;
    private static readonly DateTimeOffset Occurred = new(2026, 11, 20, 11, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReportedBefore = new(2026, 11, 20, 12, 0, 2, TimeSpan.Zero);

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset? now = null)
        {
            Time = new FakeTimeProvider(now ?? Now);
            Service = new OutcomeService(Store, Store, Time, Logger);
        }

        public FakeDecisionAuditStore Store { get; } = new();

        public FakeTimeProvider Time { get; }

        public CapturingLogger<OutcomeService> Logger { get; } = new();

        public OutcomeService Service { get; }

        public Task<OutcomeResponseDto> RecordAsync(OutcomeRequestDto request, Guid? decisionId = null) =>
            Service.RecordAsync(decisionId ?? DecisionId, request, CancellationToken.None);
    }

    private static OutcomeRequestDto Request(DecisionOutcome outcome, DateTimeOffset occurredAt) =>
        new() { Outcome = outcome, OccurredAt = occurredAt };

    private static DecisionOutcomeRecord Existing(DecisionOutcome outcome = DecisionOutcome.NO_SHOW, DateTimeOffset? occurredAt = null) =>
        new(DecisionId, outcome, occurredAt ?? Occurred, ReportedBefore);

    public static TheoryData<string> Scenarios => new()
    {
        "New",
        "NewWithOffset",
        "ReplaySubMicrosecondTicks",
        "DifferentOutcome",
        "DifferentOccurredAt",
        "LostRaceSamePayload",
        "LostRaceDifferentPayload",
    };

    /// <summary>Mới → ghi và trả 200; trùng payload (khác tick dưới µs) → 200 với <c>reportedAt</c> cũ; khác payload → 409; thua tranh chấp → theo bản thắng.</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [MemberData(nameof(Scenarios))]
    public async Task Outcome_Record_Replay_Conflict_Theory(string scenario)
    {
        var fixture = new Fixture(Now.AddTicks(7));
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));
        var request = Request(DecisionOutcome.NO_SHOW, Occurred);
        switch (scenario)
        {
            case "NewWithOffset":
                request = Request(DecisionOutcome.NO_SHOW, Occurred.ToOffset(TimeSpan.FromHours(7)));
                break;
            case "ReplaySubMicrosecondTicks":
                fixture.Store.SeedOutcome(Existing());
                request = Request(DecisionOutcome.NO_SHOW, Occurred.AddTicks(5).ToOffset(TimeSpan.FromHours(7)));
                break;
            case "DifferentOutcome":
                fixture.Store.SeedOutcome(Existing(DecisionOutcome.COMPLETED));
                break;
            case "DifferentOccurredAt":
                fixture.Store.SeedOutcome(Existing(occurredAt: Occurred.AddMinutes(1)));
                break;
            case "LostRaceSamePayload":
                fixture.Store.SimulateLostOutcomeRace(Existing());
                break;
            case "LostRaceDifferentPayload":
                fixture.Store.SimulateLostOutcomeRace(Existing(DecisionOutcome.COMPLETED));
                break;
        }

        var act = () => fixture.RecordAsync(request);

        switch (scenario)
        {
            case "New":
            case "NewWithOffset":
                var created = await act();
                created.DecisionId.Should().Be(DecisionId);
                created.Outcome.Should().Be(DecisionOutcome.NO_SHOW);
                created.OccurredAt.Should().Be(Occurred);
                created.OccurredAt.Offset.Should().Be(TimeSpan.Zero, "thời điểm trong response trả theo UTC");
                created.ReportedAt.Should().Be(Now, "một nowUtc đã cắt về µs");
                created.ReportedAt.Offset.Should().Be(TimeSpan.Zero);
                fixture.Store.Outcomes[DecisionId].ReportedAt.Should().Be(Now);
                fixture.Store.Outcomes[DecisionId].OccurredAt.Offset.Should().Be(TimeSpan.Zero);
                fixture.Store.OutcomeInsertCount.Should().Be(1);
                break;
            case "ReplaySubMicrosecondTicks":
                var replay = await act();
                replay.Outcome.Should().Be(DecisionOutcome.NO_SHOW);
                replay.OccurredAt.Should().Be(Occurred);
                replay.ReportedAt.Should().Be(ReportedBefore, "replay trả bản đã lưu");
                fixture.Store.OutcomeInsertCount.Should().Be(0);
                break;
            case "DifferentOutcome":
            case "DifferentOccurredAt":
                (await act.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.OutcomeAlreadyRecorded);
                fixture.Store.OutcomeInsertCount.Should().Be(0);
                break;
            case "LostRaceSamePayload":
                var winnerReplay = await act();
                winnerReplay.ReportedAt.Should().Be(ReportedBefore, "trả bản của request thắng");
                fixture.Store.OutcomeInsertCount.Should().Be(1);
                break;
            case "LostRaceDifferentPayload":
                (await act.Should().ThrowAsync<DecisionStateConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.OutcomeAlreadyRecorded);
                fixture.Store.OutcomeInsertCount.Should().Be(1);
                break;
        }
    }

    /// <summary>Không có quyết định, hoặc là <c>SHADOW</c> (không bao giờ lộ cho client) → <see cref="DecisionNotFoundException"/>, không ghi gì.</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outcome_ShadowOrMissing_IsNotFound_Theory(bool shadow)
    {
        var fixture = new Fixture();
        if (shadow)
        {
            fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, role: ExecutionRole.SHADOW));
        }

        var act = () => fixture.RecordAsync(Request(DecisionOutcome.COMPLETED, Occurred));

        await act.Should().ThrowAsync<DecisionNotFoundException>();
        fixture.Store.OutcomeInsertCount.Should().Be(0);
    }

    /// <summary>Outcome áp cho mọi quyết định <c>PRIMARY</c> bất kể trạng thái (kể cả đã APPROVED, không cần override).</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [InlineData(DecisionStatus.APPROVED)]
    [InlineData(DecisionStatus.NEED_APPROVAL)]
    public async Task Outcome_AnyPrimaryStatus_IsRecorded_Theory(DecisionStatus status)
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId, status: status));

        var response = await fixture.RecordAsync(Request(DecisionOutcome.COMPLETED, Occurred));

        response.Outcome.Should().Be(DecisionOutcome.COMPLETED);
        fixture.Store.Outcomes.Should().ContainKey(DecisionId);
    }

    /// <summary>Log Information khi thành công có decisionId, outcome và cờ replay.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public async Task Outcome_Success_LogsInformationWithReplayFlag()
    {
        var fixture = new Fixture();
        fixture.Store.Seed(GovernanceTestData.Execution(DecisionId));

        await fixture.RecordAsync(Request(DecisionOutcome.NO_SHOW, Occurred));
        await fixture.RecordAsync(Request(DecisionOutcome.NO_SHOW, Occurred));

        var entries = fixture.Logger.Entries;
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(entry => entry.Level == LogLevel.Information
            && entry.Message.Contains(DecisionId.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("NO_SHOW", StringComparison.Ordinal));
        entries[0].Message.Should().Contain("False");
        entries[1].Message.Should().Contain("True");
    }
}
