using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using TADSS.Application.Common;
using TADSS.Infrastructure.Resilience;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// FI-05 / UT-07: <see cref="IsolatedStrategyInvoker"/> cô lập luồng request khỏi strategy không hợp tác (chặn đồng bộ hoặc bỏ qua token):
/// token hủy thì lời gọi trả về ngay bằng <see cref="OperationCanceledException"/>, task của strategy bị bỏ rơi nhưng exception muộn của nó vẫn được quan sát.
/// </summary>
public class IsolatedStrategyInvokerTests
{
    private const string AbandonedFaultMarker = "loi-muon-cua-task-bi-bo-roi";

    /// <summary>Lưới an toàn của ca strategy không hợp tác: chỉ hết hạn khi invoker (sai) chờ strategy bị chặn tới khi test mở cổng.</summary>
    private static readonly TimeSpan InvokerReturnLimit = TimeSpan.FromSeconds(30);

    private static StrategyDecision Approved() =>
        StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng thành viên."]);

    private static Task<StrategyDecision?> InvokeAsync(
        IsolatedStrategyInvoker invoker,
        DelegateStrategy strategy,
        CancellationToken token) =>
        invoker.InvokeAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), token);

    /// <summary>Strategy hợp tác trả quyết định ngay: invoker trả đúng quyết định đó và không ghi metric bỏ rơi.</summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task InvokeAsync_CooperativeStrategy_ReturnsDecision()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        var expected = Approved();
        var strategy = new DelegateStrategy((_, _, _) => Task.FromResult(expected));

        var decision = await InvokeAsync(invoker, strategy, CancellationToken.None);

        decision.Should().BeSameAs(expected);
        recorder.Sum(TadssMetricNames.StrategyAbandoned).Should().Be(0);
    }

    /// <summary>
    /// Strategy chặn đồng bộ (bỏ qua token) cho tới khi test mở cổng — tức là không bao giờ tự xong trong lúc lời gọi đang chờ; token hủy sau 100 ms:
    /// lời gọi vẫn kết thúc bằng hủy (nếu invoker chờ strategy thì hết <see cref="InvokerReturnLimit"/> trước → <see cref="TimeoutException"/>, test đỏ)
    /// và ghi một lần bỏ rơi. Không đo bằng đồng hồ thật nên không phụ thuộc máy bận.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task InvokeAsync_SyncBlockingStrategyIgnoringToken_ReturnsWhenTokenCancelled()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new DelegateStrategy(
            (_, _, _) =>
            {
                release.Task.Wait();
                return Task.FromResult(Approved());
            },
            strategyName: "BlockingStrategy");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            var act = () => InvokeAsync(invoker, strategy, cts.Token).WaitAsync(InvokerReturnLimit);
            await act.Should().ThrowAsync<OperationCanceledException>("lời gọi không được chờ strategy đang chặn đồng bộ");
        }
        finally
        {
            release.TrySetResult();
        }

        recorder.Sum(TadssMetricNames.StrategyAbandoned).Should().Be(1);
        recorder.Sum(TadssMetricNames.StrategyAbandoned, ("strategy", "BlockingStrategy")).Should().Be(1);
    }

    /// <summary>Strategy <c>await</c> một việc không truyền token và chỉ xong khi test mở cổng: như ca chặn đồng bộ, lời gọi trả về khi token hủy.</summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task InvokeAsync_AsyncStrategyIgnoringToken_ReturnsWhenTokenCancelled()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new DelegateStrategy(async (_, _, _) =>
        {
            await release.Task;
            return Approved();
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            var act = () => InvokeAsync(invoker, strategy, cts.Token).WaitAsync(InvokerReturnLimit);
            await act.Should().ThrowAsync<OperationCanceledException>("lời gọi không được chờ strategy bỏ qua token");
        }
        finally
        {
            release.TrySetResult();
        }

        recorder.Sum(TadssMetricNames.StrategyAbandoned).Should().Be(1);
    }

    /// <summary>
    /// Task bị bỏ rơi ném lỗi sau khi bị hủy; ép GC + <c>WaitForPendingFinalizers</c>: <see cref="TaskScheduler.UnobservedTaskException"/>
    /// không được gọi cho task đó (continuation của invoker đã đọc exception). Lọc theo message riêng của test nên không lẫn test khác.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task InvokeAsync_AbandonedTaskFaultsLater_NoUnobservedException()
    {
        var unobserved = new List<string>();

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            foreach (var inner in args.Exception.Flatten().InnerExceptions)
            {
                if (inner.Message.Contains(AbandonedFaultMarker, StringComparison.Ordinal))
                {
                    lock (unobserved)
                    {
                        unobserved.Add(inner.Message);
                    }
                }
            }
        }

        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await AbandonStrategyThatFaultsLaterAsync(faulted);
            await faulted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Chờ continuation (ExecuteSynchronously) chạy xong trước khi ép GC
            await Task.Delay(100);

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        lock (unobserved)
        {
            unobserved.Should().BeEmpty("task bị bỏ rơi đã được gắn continuation đọc exception");
        }
    }

    /// <summary>Tách khỏi test để mọi tham chiếu tới task bị bỏ rơi nằm ngoài stack của test trước khi ép GC.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task AbandonStrategyThatFaultsLaterAsync(TaskCompletionSource faulted)
    {
        using var metrics = new TadssMetrics();
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        var strategy = new DelegateStrategy(async (_, _, _) =>
        {
            await Task.Delay(300);
            faulted.TrySetResult();
            throw new InvalidOperationException(AbandonedFaultMarker);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => InvokeAsync(invoker, strategy, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Đua giữa hủy và hoàn tất: token đã hủy sẵn trong khi strategy lỗi gần như tức thì, nên task thường vừa xong (lỗi) đúng lúc invoker bắt
    /// <see cref="OperationCanceledException"/>. Dù task đã xong hay chưa, exception của nó vẫn phải được quan sát (không <c>UnobservedTaskException</c>).
    /// Cửa sổ đua không điều khiển được tuyệt đối nên test lặp nhiều lượt thay vì khẳng định từng lượt rơi vào nhánh nào.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task InvokeAsync_CancelRacesWithFaultingCompletion_NoUnobservedException()
    {
        const string marker = "loi-tuc-thi-dua-voi-huy";
        var unobserved = new List<string>();

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            foreach (var inner in args.Exception.Flatten().InnerExceptions)
            {
                if (inner.Message.Contains(marker, StringComparison.Ordinal))
                {
                    lock (unobserved)
                    {
                        unobserved.Add(inner.Message);
                    }
                }
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            await RunPreCancelledAttemptsAsync(marker, attempts: 300);
            await Task.Delay(200);

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        lock (unobserved)
        {
            unobserved.Should().BeEmpty("mọi lần bắt hủy đều gắn continuation quan sát, kể cả khi task vừa xong lỗi");
        }
    }

    /// <summary>Tách khỏi test để mọi tham chiếu tới các task đã bỏ nằm ngoài stack của test trước khi ép GC.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunPreCancelledAttemptsAsync(string marker, int attempts)
    {
        using var metrics = new TadssMetrics();
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        var strategy = new DelegateStrategy((_, _, _) => Task.FromException<StrategyDecision>(new InvalidOperationException(marker)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                await InvokeAsync(invoker, strategy, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Mong đợi: token đã hủy. Một số lượt WaitAsync thấy task xong lỗi trước và ném chính lỗi đó
            }
            catch (InvalidOperationException)
            {
                // Lượt mà task đã xong trước khi WaitAsync xét token: exception đã được await nên đã quan sát
            }
        }
    }

    /// <summary>Strategy ném đồng bộ hoặc bất đồng bộ: exception lan ra nguyên vẹn (cùng kiểu, cùng message) để pipeline xử lý như cũ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task InvokeAsync_StrategyThrows_PropagatesOriginalException()
    {
        using var metrics = new TadssMetrics();
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        var syncThrowing = new DelegateStrategy((_, _, _) => throw new InvalidOperationException("dong-bo"));
        var asyncThrowing = new DelegateStrategy((_, _, _) => Task.FromException<StrategyDecision>(new ArgumentException("bat-dong-bo")));

        var sync = () => InvokeAsync(invoker, syncThrowing, CancellationToken.None);
        var async = () => InvokeAsync(invoker, asyncThrowing, CancellationToken.None);

        (await sync.Should().ThrowExactlyAsync<InvalidOperationException>()).WithMessage("dong-bo");
        (await async.Should().ThrowExactlyAsync<ArgumentException>()).WithMessage("bat-dong-bo");
    }

    /// <summary>Strategy trả <c>null</c>: invoker trả <c>null</c>, pipeline mới là nơi biến nó thành lỗi hợp đồng.</summary>
    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task InvokeAsync_StrategyReturnsNull_ReturnsNull()
    {
        using var metrics = new TadssMetrics();
        var invoker = new IsolatedStrategyInvoker(metrics, NullLogger.Instance);
        // Strategy lỗi trả null dù kiểu trả về không cho phép: ép qua object để mô phỏng đúng tình huống đó
        var strategy = new DelegateStrategy((_, _, _) => (Task<StrategyDecision>)(object)Task.FromResult<StrategyDecision?>(null));

        var decision = await InvokeAsync(invoker, strategy, CancellationToken.None);

        decision.Should().BeNull("pipeline mới là nơi biến null thành lỗi hợp đồng");
    }
}
