using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Common;

namespace TADSS.Infrastructure.Resilience;

/// <summary>
/// Bước trong cùng của pipeline cho <b>mọi</b> strategy (A-06): chạy strategy trên thread pool rồi chờ bằng <c>WaitAsync(token)</c>,
/// nên strategy chặn đồng bộ hoặc bỏ qua token cũng không giữ được luồng request quá hạn của token (đã gộp inner timeout,
/// outer timeout và hủy của caller). Token hủy khi task chưa xong thì task bị <b>bỏ rơi</b>: ghi metric, quan sát exception muộn
/// của nó (không sinh <c>UnobservedTaskException</c>) rồi để <see cref="OperationCanceledException"/> lan ra như cũ.
/// <para>
/// Giới hạn: TADSS chỉ <b>giới hạn thời gian chờ</b> chứ không dừng được strategy không hợp tác; CPU của task bị bỏ rơi vẫn bị
/// tiêu thụ tới khi nó tự kết thúc, và nhiều task bị bỏ rơi cùng lúc có thể làm cạn thread pool.
/// </para>
/// </summary>
internal sealed class IsolatedStrategyInvoker(TadssMetrics metrics, ILogger logger)
{
    /// <summary>
    /// Chạy <paramref name="strategy"/>. Exception của strategy (đồng bộ hay bất đồng bộ) lan ra nguyên vẹn; strategy trả <c>null</c> thì trả <c>null</c>
    /// (pipeline coi là lỗi hợp đồng).
    /// </summary>
    public async Task<StrategyDecision?> InvokeAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        DecisionContext context,
        CancellationToken cancellationToken)
    {
        // Phần đồng bộ của strategy chạy trên luồng riêng (LongRunning), không trên thread pool: strategy chặn luồng (không hợp tác) mà dùng luồng của pool
        // thì trên máy ít CPU (pool sẵn có ~ số CPU) vài lượt bị bỏ rơi là đủ làm cạn pool, timer timeout và fallback phải chờ pool thêm luồng
        // (~0,5 s/luồng) nên request vượt Outer timeout thành 504 thay vì fallback (FI-05 trượt khi DOTNET_PROCESSOR_COUNT=2).
        // CancellationToken.None: hủy do WaitAsync xử lý, không để task chưa kịp chạy bị hủy mà không ai quan sát
        var task = Task.Factory.StartNew(
                () => strategy.EvaluateAsync(reservation, context, cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var strategyName = strategy.StrategyName;
            // Đua giữa hủy và hoàn tất: WaitAsync đã ném vì token hủy nhưng task có thể vừa xong (kể cả lỗi) ngay lúc này.
            // Vì vậy luôn gắn continuation quan sát exception (vô hại nếu task đã xong sạch); chỉ ghi metric "bỏ rơi" khi task thật sự chưa xong.
            if (!task.IsCompleted)
            {
                metrics.RecordStrategyAbandoned(strategyName);
            }

            // Quan sát exception của task bị bỏ rơi hoặc vừa lỗi để không sinh UnobservedTaskException
            _ = task.ContinueWith(
                (finished, state) =>
                {
                    if (finished.Exception is { } exception)
                    {
                        logger.LogDebug(exception, "Strategy {StrategyName} kết thúc lỗi sau khi token đã hủy.", state);
                    }
                },
                strategyName,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }
}
