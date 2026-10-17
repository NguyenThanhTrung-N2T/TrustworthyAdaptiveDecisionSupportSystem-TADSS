using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
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
/// UT-11: <see cref="DecisionPathWarmup"/> làm nóng đường xử lý quyết định lúc khởi động bằng test double — mở song song đủ số kết nối qua truy vấn
/// idempotency chỉ đọc, đọc cache, chạy mọi strategy đã đăng ký qua pipeline và dựng bản ghi; KHÔNG ghi audit, không ghi cache; lỗi hay vượt
/// ngân sách chỉ ghi cảnh báo, không bao giờ làm hỏng khởi động; chỉ hủy của chính caller được lan ra.
/// </summary>
public class DecisionPathWarmupTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    private static RegisteredStrategy Approve(string name) =>
        new(new DelegateStrategy((_, _, _) => Task.FromResult(StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng."])), strategyName: name), "0123456789abcdef");

    /// <summary>Bộ phụ thuộc giả dùng chung cho một lần dựng <see cref="DecisionPathWarmup"/>.</summary>
    private sealed class Fixture
    {
        public Fixture(WarmupOptions options, FakeStrategyPipeline? pipeline = null, TimeProvider? time = null)
        {
            Pipeline = pipeline ?? new FakeStrategyPipeline();
            var strategies = new[] { Approve("WarmA"), Approve("WarmB") };
            var services = new ServiceCollection().AddScoped<IDecisionEvaluationStore>(_ => Store).BuildServiceProvider();
            Warmup = new DecisionPathWarmup(
                services.GetRequiredService<IServiceScopeFactory>(),
                new StrategyRegistry(strategies, NullLogger<StrategyRegistry>.Instance),
                new FakeStrategyRouter(strategies[0]),
                Pipeline,
                Cache,
                new DecisionResponseComposer(new DepositCalculator(), new ApprovalWindowPolicy(MsOptions.Create(new ApprovalOptions()))),
                DomainTestData.StandardCalendar(),
                MsOptions.Create(options),
                time ?? new FakeTimeProvider(Now),
                Logger);
        }

        public FakeDecisionAuditStore Store { get; } = new();

        public FakeStrategyPipeline Pipeline { get; }

        public FakeStrategyDecisionCache Cache { get; } = new();

        public CapturingLogger<DecisionPathWarmup> Logger { get; } = new();

        public DecisionPathWarmup Warmup { get; }
    }

    /// <summary>Bật: mỗi vòng chạy cả hai strategy qua pipeline, mở đủ số truy vấn idempotency, đọc cache; không ghi audit, không ghi cache.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task RunAsync_Enabled_ExercisesDecisionPathWithoutPersistingOrCaching()
    {
        var fixture = new Fixture(new WarmupOptions { DatabaseConnections = 4, Iterations = 3 });

        var completed = await fixture.Warmup.RunAsync(CancellationToken.None);

        completed.Should().BeTrue();
        fixture.Pipeline.InvocationCount.Should().Be(6, "3 vòng × 2 strategy đã đăng ký");
        fixture.Store.FindCount.Should().Be(4, "mỗi kết nối cần mở là một truy vấn idempotency chỉ đọc chạy song song");
        fixture.Store.InsertCount.Should().Be(0, "làm nóng không bao giờ ghi audit (bảng append-only, bất biến)");
        fixture.Cache.GetKeys.Should().NotBeEmpty();
        fixture.Cache.Sets.Should().BeEmpty("làm nóng không ghi cache");
        fixture.Logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Information);
    }

    /// <summary>Tắt: không chạm store, cache hay pipeline.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task RunAsync_Disabled_DoesNothing()
    {
        var fixture = new Fixture(new WarmupOptions { Enabled = false });

        var completed = await fixture.Warmup.RunAsync(CancellationToken.None);

        completed.Should().BeFalse();
        fixture.Pipeline.InvocationCount.Should().Be(0);
        fixture.Store.FindCount.Should().Be(0);
        fixture.Cache.GetKeys.Should().BeEmpty();
    }

    /// <summary>Một bước lỗi: ghi cảnh báo kèm tên kiểu exception, các bước khác vẫn chạy, không ném.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task RunAsync_StepThrows_LogsWarningAndKeepsWarmingOtherSteps()
    {
        var pipeline = new FakeStrategyPipeline((_, _, _, _) => throw new InvalidOperationException("strategy hỏng"));
        var fixture = new Fixture(new WarmupOptions { DatabaseConnections = 2, Iterations = 1 }, pipeline);

        var completed = await fixture.Warmup.RunAsync(CancellationToken.None);

        completed.Should().BeFalse();
        fixture.Store.FindCount.Should().Be(2);
        fixture.Logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(nameof(InvalidOperationException)));
        fixture.Logger.Entries.Should().NotContain(entry => entry.Message.Contains("strategy hỏng"), "chỉ ghi tên kiểu, message có thể chứa chi tiết nội bộ");
    }

    /// <summary>Vượt ngân sách thời gian: dừng, ghi cảnh báo, không ném (instance vẫn khởi động).</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task RunAsync_ExceedsTimeBudget_StopsWithWarning()
    {
        // Đồng hồ giả: chỉ tua qua ngân sách sau khi pipeline đã thật sự treo. Cả chuỗi hủy phải hoàn tất NGAY trong Advance, trên luồng test,
        // không chờ luồng nào khác: khi chạy cả bộ trên CI ít CPU, thread pool và các khe của xUnit bị test khác chiếm hàng chục giây nên
        // giới hạn 30 s bị vượt dù logic đúng. Vì vậy pipeline treo là task chỉ hủy qua token (lambda async sẽ bắt SynchronizationContext của
        // xUnit) và Advance chạy khi luồng không có SynchronizationContext (có context khác mặc định thì .NET không chạy tiếp continuation
        // ConfigureAwait(false) của mã làm nóng ngay tại chỗ mà đẩy sang thread pool)
        var time = new FakeTimeProvider(Now);
        var entered = new TaskCompletionSource();
        var pipeline = new FakeStrategyPipeline((_, _, _, token) =>
        {
            entered.TrySetResult();
            var hung = new TaskCompletionSource<StrategyExecutionOutcome>();
            token.Register(() => hung.TrySetCanceled(token));
            return hung.Task;
        });
        var options = new WarmupOptions { DatabaseConnections = 1, Iterations = 1, TimeoutMs = WarmupOptionsValidator.MinTimeoutMs };
        var fixture = new Fixture(options, pipeline, time);

        var run = fixture.Warmup.RunAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var testContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            time.Advance(TimeSpan.FromMilliseconds(options.TimeoutMs));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(testContext);
        }

        (await run.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeFalse();
        fixture.Logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("ngân sách"));
    }

    /// <summary>Hủy của caller (host dừng khi đang khởi động) được lan ra, không bị nuốt như lỗi làm nóng.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task RunAsync_CallerCancels_PropagatesCancellation()
    {
        var fixture = new Fixture(new WarmupOptions());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => fixture.Warmup.RunAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
