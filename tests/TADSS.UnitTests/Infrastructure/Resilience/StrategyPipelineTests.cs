using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Time.Testing;
using Polly.Timeout;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Resilience;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// UT-07 (phần pipeline): <see cref="StrategyPipeline"/> chạy strategy v2 qua Outer timeout → Fallback → Retry →
/// Circuit breaker → Inner timeout và biến F2–F4 (timeout, lỗi, circuit mở) thành quyết định fallback an toàn.
/// Các ca timeout dùng strategy giả chờ vô hạn nhưng tôn trọng token cùng timeout nhỏ nên kết quả không phụ thuộc
/// tốc độ máy; ca đo thời gian dùng <see cref="FakeTimeProvider"/>.
/// </summary>
public class StrategyPipelineTests
{
    private const string SensitiveMessage = "chi-tiet-nhay-cam-KHONG-DUOC-LOT-RA";

    private static ResilienceOptions SmallOptions(
        int maxRetryAttempts = 2,
        int innerTimeoutMs = 30,
        int outerTimeoutMs = 5_000,
        int minimumThroughput = 10) => new()
        {
            OuterTimeoutMs = outerTimeoutMs,
            InnerTimeoutMs = innerTimeoutMs,
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelayMs = 1,
            CircuitBreakerMinimumThroughput = minimumThroughput,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerBreakDurationSeconds = 60
        };

    private static StrategyPipeline Build(ResilienceOptions options, TimeProvider? timeProvider = null) =>
        new(MsOptions.Create(options), timeProvider ?? TimeProvider.System, NullLogger<StrategyPipeline>.Instance);

    private static StrategyDecision Approved() =>
        StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng thành viên."]);

    /// <summary>Chờ vô hạn nhưng tôn trọng token: chỉ kết thúc (bằng hủy) khi timeout hoặc caller hủy.</summary>
    private static async Task<StrategyDecision> WaitForever(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException("Không được tới đây: Task.Delay vô hạn chỉ kết thúc bằng hủy.");
    }

    private static Task<StrategyDecision> Throwing() =>
        Task.FromException<StrategyDecision>(new InvalidOperationException(SensitiveMessage));

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_StrategySucceeds_Automated()
    {
        var expected = Approved();
        var strategy = new DelegateStrategy((_, _, _) => Task.FromResult(expected));
        var pipeline = Build(SmallOptions());

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        outcome.FallbackUsed.Should().BeFalse();
        outcome.FailureReason.Should().BeNull();
        outcome.Decision.Should().BeSameAs(expected);
        outcome.Decision.DepositRate.Should().Be(0.5m);
        outcome.ExecutionTimeMs.Should().BeGreaterThanOrEqualTo(0);
        strategy.InvocationCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_PassesContextToStrategy()
    {
        var strategy = new DelegateStrategy((_, _, _) => Task.FromResult(Approved()));
        var reservation = DomainTestData.CreateReservation();
        var context = DomainTestData.CreateContext();
        var pipeline = Build(SmallOptions());

        await pipeline.ExecuteAsync(strategy, reservation, context, CancellationToken.None);

        strategy.LastReservation.Should().BeSameAs(reservation);
        strategy.LastContext.Should().BeSameAs(context);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_FailsOnceThenSucceeds_RetriedAutomated()
    {
        var calls = 0;
        var strategy = new DelegateStrategy((_, _, _) =>
            Interlocked.Increment(ref calls) == 1 ? Throwing() : Task.FromResult(Approved()));
        var pipeline = Build(SmallOptions());

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        outcome.FallbackUsed.Should().BeFalse();
        outcome.FailureReason.Should().BeNull();
        strategy.InvocationCount.Should().Be(2);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_AlwaysThrows_FallbackException()
    {
        var options = SmallOptions(maxRetryAttempts: 2);
        var strategy = new DelegateStrategy((_, _, _) => Throwing());
        var pipeline = Build(options);

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.Decision.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        outcome.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        strategy.InvocationCount.Should().Be(options.MaxRetryAttempts + 1);
        outcome.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyException)
            .And.Contain(nameof(InvalidOperationException))
            .And.NotContain(SensitiveMessage);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_StrategyThrowsDomainInvariant_FallbackExceptionWithoutDetails()
    {
        // Factory StrategyDecision ném DomainInvariantException (tỷ lệ cọc ngoài [0, 1]) bên trong strategy
        var strategy = new DelegateStrategy((_, _, _) =>
            Task.FromResult(StrategyDecision.Approve(1.5m, ReasonCodes.TierDeposit, [SensitiveMessage])));
        var pipeline = Build(SmallOptions());

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        outcome.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyException)
            .And.Contain(nameof(DomainInvariantException))
            .And.NotContain("INVALID_DEPOSIT_RATE");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_StrategyReturnsNull_FallbackException()
    {
        var options = SmallOptions(maxRetryAttempts: 2);
        // Strategy lỗi trả null dù kiểu trả về không cho phép: ép qua object để mô phỏng đúng tình huống đó
        var strategy = new DelegateStrategy((_, _, _) => (Task<StrategyDecision>)(object)Task.FromResult<StrategyDecision?>(null));
        var pipeline = Build(options);

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        outcome.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyException);
        strategy.InvocationCount.Should().Be(options.MaxRetryAttempts + 1);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_AlwaysSlow_FallbackTimeout()
    {
        var options = SmallOptions(maxRetryAttempts: 1, innerTimeoutMs: 20);
        var strategy = new DelegateStrategy((_, _, token) => WaitForever(token));
        var pipeline = Build(options);

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.Decision.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        outcome.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyTimeout);
        outcome.FailureReason.Should().Be(FailureReasonPrefixes.FormatTimeout(options.InnerTimeoutMs));
        outcome.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyTimeout);
        strategy.InvocationCount.Should().Be(options.MaxRetryAttempts + 1);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_CircuitOpen_FallbackWithoutInvokingStrategy()
    {
        var options = SmallOptions(maxRetryAttempts: 0, minimumThroughput: 2);
        var strategy = new DelegateStrategy((_, _, _) => Throwing());
        var pipeline = Build(options);
        var reservation = DomainTestData.CreateReservation();
        var context = DomainTestData.CreateContext();

        var first = await pipeline.ExecuteAsync(strategy, reservation, context, CancellationToken.None);
        var second = await pipeline.ExecuteAsync(strategy, reservation, context, CancellationToken.None);
        var invocationsBeforeOpen = strategy.InvocationCount;
        var third = await pipeline.ExecuteAsync(strategy, reservation, context, CancellationToken.None);

        first.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        second.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        invocationsBeforeOpen.Should().Be(2);
        third.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        third.FallbackUsed.Should().BeTrue();
        third.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackCircuitOpen);
        third.FailureReason.Should().Be(FailureReasonPrefixes.FormatCircuitBreakerOpen());
        strategy.InvocationCount.Should().Be(invocationsBeforeOpen, "circuit mở thì không chạm strategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_CallerCancelled_PropagatesOperationCanceled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new DelegateStrategy((_, _, token) =>
        {
            started.TrySetResult();
            return WaitForever(token);
        });
        var pipeline = Build(SmallOptions(innerTimeoutMs: 30_000, outerTimeoutMs: 60_000));
        using var cts = new CancellationTokenSource();

        var execution = pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        var thrown = await execution.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<DecisionTimeoutException>("hủy từ caller không bị đổi thành lỗi timeout");
        strategy.InvocationCount.Should().Be(1, "hủy từ caller không được retry hay fallback");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_StrategyThrowsTaskCanceledWhileCallerLive_FallbackException()
    {
        // F3: HttpClient timeout của strategy ném TaskCanceledException dù caller không hủy -> lỗi strategy, không phải hủy
        var options = SmallOptions(maxRetryAttempts: 2);
        var strategy = new DelegateStrategy((_, _, _) =>
            Task.FromException<StrategyDecision>(new TaskCanceledException(SensitiveMessage)));
        var pipeline = Build(options);

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.Decision.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        outcome.Decision.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        outcome.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyException)
            .And.Contain(nameof(TaskCanceledException))
            .And.NotContain(SensitiveMessage);
        strategy.InvocationCount.Should().Be(options.MaxRetryAttempts + 1, "lỗi của strategy được retry như mọi lỗi khác");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_StrategyThrowsOperationCanceledOnceWhileCallerLive_RetriedAutomated()
    {
        var calls = 0;
        var strategy = new DelegateStrategy((_, _, _) =>
            Interlocked.Increment(ref calls) == 1
                ? Task.FromException<StrategyDecision>(new OperationCanceledException())
                : Task.FromResult(Approved()));
        var pipeline = Build(SmallOptions());

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        outcome.FallbackUsed.Should().BeFalse();
        strategy.InvocationCount.Should().Be(2);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_StrategyThrowsOperationCanceledAfterCallerCancelled_Throws()
    {
        // Caller đã hủy rồi strategy mới ném OCE (không dùng token): vẫn là hủy, không retry và không fallback
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var strategy = new DelegateStrategy(async (_, _, _) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
            throw new OperationCanceledException();
        });
        var pipeline = Build(SmallOptions(innerTimeoutMs: 30_000, outerTimeoutMs: 60_000));

        var execution = pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await execution.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        strategy.InvocationCount.Should().Be(1, "hủy từ caller không được retry hay fallback");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_OuterTimeoutExceeded_ThrowsDecisionTimeoutException()
    {
        // Outer nhỏ hơn hẳn Inner nên Outer luôn hết hạn trước; Outer nằm ngoài Fallback nên lỗi lan ra (504), đã dịch sang kiểu của Application
        var options = SmallOptions(maxRetryAttempts: 0, innerTimeoutMs: 60_000, outerTimeoutMs: 50);
        var strategy = new DelegateStrategy((_, _, token) => WaitForever(token));
        var pipeline = Build(options);

        var act = () => pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<DecisionTimeoutException>();
        thrown.Which.InnerException.Should().BeOfType<TimeoutRejectedException>("giữ exception gốc của Polly để chẩn đoán");
        thrown.Which.Message.Should().NotBeNullOrWhiteSpace().And.NotContain("Polly");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_OuterTimeoutWhileCallerCancelled_PrefersCancellation()
    {
        // Tất định: Outer (50 ms) hết hạn trước Inner (60 s). Timeout của Polly là kiểu hợp tác nên strategy phải tôn trọng token;
        // khi token (do Outer timeout hủy) kích hoạt, strategy hủy luôn CTS của caller TRƯỚC khi ném lại.
        var options = SmallOptions(maxRetryAttempts: 0, innerTimeoutMs: 60_000, outerTimeoutMs: 50);
        using var callerCts = new CancellationTokenSource();
        var strategy = new DelegateStrategy(async (_, _, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                await callerCts.CancelAsync();
                throw;
            }

            throw new InvalidOperationException("Không được tới đây: Task.Delay vô hạn chỉ kết thúc bằng hủy.");
        });
        var pipeline = Build(options);

        var act = () => pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), callerCts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<DecisionTimeoutException>();
        callerCts.IsCancellationRequested.Should().BeTrue("caller đã hủy nên hủy được ưu tiên hơn timeout");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_CallerCancelsAfterOuterTimeoutDecided_PrefersCancellation()
    {
        // Ca cửa sổ hẹp: Polly đã quyết định timeout (callback OnTimeout ghi log Error chạy ngay trước khi ném TimeoutRejectedException)
        // rồi caller mới hủy. Logger giả hủy CTS của caller đúng lúc đó nên TimeoutRejectedException lan tới catch với token caller đã hủy.
        var options = SmallOptions(maxRetryAttempts: 0, innerTimeoutMs: 60_000, outerTimeoutMs: 50);
        using var callerCts = new CancellationTokenSource();
        var logger = new CancelOnErrorLogger(callerCts);
        var pipeline = new StrategyPipeline(MsOptions.Create(options), TimeProvider.System, logger);
        var strategy = new DelegateStrategy((_, _, token) => WaitForever(token));

        var act = () => pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), callerCts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<DecisionTimeoutException>();
        thrown.Which.CancellationToken.Should().Be(callerCts.Token, "exception gắn token của caller để handler coi là client hủy");
        logger.ErrorLogged.Should().BeTrue("kịch bản phải đi qua nhánh Outer timeout của Polly");
    }

    /// <summary>Logger giả hủy CTS của caller ngay khi nhận bản ghi mức Error (OnTimeout của Outer timeout).</summary>
    private sealed class CancelOnErrorLogger(CancellationTokenSource callerCts) : ILogger<StrategyPipeline>
    {
        public bool ErrorLogged { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                ErrorLogged = true;
                callerCts.Cancel();
            }
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_MeasuresElapsedWithTimeProvider()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero));
        var strategy = new DelegateStrategy((_, _, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(7));
            return Task.FromResult(Approved());
        });
        var pipeline = Build(SmallOptions(), clock);

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionTimeMs.Should().Be(7);
        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_ElapsedHasFractionalMilliseconds_RoundsDown()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero));
        var strategy = new DelegateStrategy((_, _, _) =>
        {
            clock.Advance(TimeSpan.FromMicroseconds(7_900));
            return Throwing();
        });
        var pipeline = Build(SmallOptions(maxRetryAttempts: 0), clock);

        var outcome = await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.ExecutionTimeMs.Should().Be(7, "làm tròn xuống và áp dụng cả cho outcome fallback");
    }
}
