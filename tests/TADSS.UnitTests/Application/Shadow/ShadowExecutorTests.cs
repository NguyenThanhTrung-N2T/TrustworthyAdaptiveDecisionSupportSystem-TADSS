using FluentAssertions;
using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Decisions;
using TADSS.Application.Exceptions;
using TADSS.Application.Options;
using TADSS.Application.Shadow;
using TADSS.UnitTests.Application.Routing;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Application.Shadow;

/// <summary>
/// IT-12 (A-11, INV-8): <see cref="ShadowExecutor"/> chạy strategy shadow qua pipeline (không cache), cùng <c>nowUtc</c> của bản <c>PRIMARY</c>, và ghi bản
/// <c>SHADOW</c> không có khóa idempotency; mọi lỗi của shadow chỉ để lại log Warning và số đo <c>failed</c>, không ném; chỉ hủy của worker lan ra.
/// </summary>
public class ShadowExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    private static ShadowWorkItem Item() =>
        new(
            ContractSamples.DeserializeRequest(ContractSamples.RequestJson),
            DomainTestData.CreateReservation(),
            RequestHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            NowUtc: Now,
            CorrelationId: "corr-0001");

    private sealed class Fixture : IDisposable
    {
        public Fixture(FakeStrategyPipeline? pipeline = null, FakeDecisionAuditStore? store = null)
        {
            Pipeline = pipeline ?? new FakeStrategyPipeline();
            Store = store ?? new FakeDecisionAuditStore();
            Metrics = new TadssMetrics();
            Recorder = new MetricRecorder(Metrics.Meter);
            Executor = new ShadowExecutor(
                MsOptions.Create(new ShadowOptions { Enabled = true, Strategy = StrategyRouterTests.Mock }),
                StrategyRouterTests.Registry(),
                Pipeline,
                new DecisionResponseComposer(
                    new DepositCalculator(),
                    new ApprovalWindowPolicy(MsOptions.Create(new ApprovalOptions()))),
                Store,
                DomainTestData.StandardCalendar(),
                Metrics,
                Logger);
        }

        public FakeStrategyPipeline Pipeline { get; }

        public FakeDecisionAuditStore Store { get; }

        public TadssMetrics Metrics { get; }

        public MetricRecorder Recorder { get; }

        public CapturingLogger<ShadowExecutor> Logger { get; } = new();

        public ShadowExecutor Executor { get; }

        public void Dispose()
        {
            Recorder.Dispose();
            Metrics.Dispose();
        }
    }

    /// <summary>Strategy khỏe: bản SHADOW có role, không khóa idempotency, cùng đơn/hash/correlation/mốc thời gian, không cache; đếm <c>recorded</c>.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task ExecuteAsync_HealthyStrategy_InsertsShadowRecord()
    {
        using var fixture = new Fixture();
        var item = Item();

        await fixture.Executor.ExecuteAsync(item, CancellationToken.None);

        var record = fixture.Store.Records.Should().ContainSingle().Subject;
        record.Role.Should().Be(ExecutionRole.SHADOW);
        record.IdempotencyKey.Should().BeNull();
        record.ReservationId.Should().Be(item.Reservation.ReservationId);
        record.RequestHash.Should().Be(item.RequestHash);
        record.CorrelationId.Should().Be(item.CorrelationId);
        record.CreatedAt.Should().Be(Now);
        record.CacheHit.Should().BeFalse();
        record.StrategyName.Should().Be(StrategyRouterTests.Mock);
        fixture.Pipeline.LastContext.Should().NotBeNull();
        fixture.Pipeline.LastContext!.EvaluatedAtUtc.Should().Be(Now, "dùng cùng nowUtc của bản PRIMARY");
        fixture.Recorder.Sum(TadssMetricNames.ShadowExecutions, ("outcome", TadssMetricNames.ShadowOutcomeRecorded)).Should().Be(1);
    }

    /// <summary>Strategy shadow lỗi: pipeline trả FALLBACK nên vẫn có bản SHADOW là FALLBACK, APPROVED cọc 100%.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task ExecuteAsync_StrategyFallsBack_InsertsFallbackShadowRecord()
    {
        var fallback = StrategyDecision.Approve(1.0m, ReasonCodes.FallbackStrategyException, ["Strategy shadow lỗi."]);
        using var fixture = new Fixture(FakeStrategyPipeline.Fallback(fallback, "FALLBACK_STRATEGY_EXCEPTION"));

        await fixture.Executor.ExecuteAsync(Item(), CancellationToken.None);

        var record = fixture.Store.Records.Should().ContainSingle().Subject;
        record.Role.Should().Be(ExecutionRole.SHADOW);
        record.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        record.FallbackUsed.Should().BeTrue();
        record.Status.Should().Be(DecisionStatus.APPROVED);
        record.DepositRate.Should().Be(1.0m);
    }

    /// <summary>Store ném lỗi lưu trữ: không ném ra ngoài, đếm <c>failed</c> và có log Warning kèm kiểu exception.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task ExecuteAsync_PersistenceFails_LogsAndCountsFailed()
    {
        var store = new FakeDecisionAuditStore();
        store.FailInsertWithPersistenceUnavailable();
        using var fixture = new Fixture(store: store);

        var act = () => fixture.Executor.ExecuteAsync(Item(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        fixture.Recorder.Sum(TadssMetricNames.ShadowExecutions, ("outcome", TadssMetricNames.ShadowOutcomeFailed)).Should().Be(1);
        fixture.Recorder.Sum(TadssMetricNames.ShadowExecutions, ("outcome", TadssMetricNames.ShadowOutcomeRecorded)).Should().Be(0);
        fixture.Logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(PersistenceUnavailableException), StringComparison.Ordinal));
    }

    /// <summary>Outer timeout của pipeline cũng là lỗi của shadow: không có bản ghi, không ném, đếm <c>failed</c>.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task ExecuteAsync_PipelineTimesOut_CountsFailedWithoutRecord()
    {
        var pipeline = new FakeStrategyPipeline((_, _, _, _) => throw new DecisionTimeoutException("Vượt Outer timeout.", null));
        using var fixture = new Fixture(pipeline);

        var act = () => fixture.Executor.ExecuteAsync(Item(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        fixture.Store.Records.Should().BeEmpty();
        fixture.Recorder.Sum(TadssMetricNames.ShadowExecutions, ("outcome", TadssMetricNames.ShadowOutcomeFailed)).Should().Be(1);
    }

    /// <summary>Worker bị hủy (tắt ứng dụng) thì <see cref="OperationCanceledException"/> lan ra và không đếm là lỗi.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task ExecuteAsync_WorkerCancelled_Propagates()
    {
        var pipeline = new FakeStrategyPipeline(async (_, _, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Không được tới đây.");
        });
        using var fixture = new Fixture(pipeline);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => fixture.Executor.ExecuteAsync(Item(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Recorder.Sum(TadssMetricNames.ShadowExecutions).Should().Be(0);
    }
}
