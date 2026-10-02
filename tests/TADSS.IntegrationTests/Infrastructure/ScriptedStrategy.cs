using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Strategy v2 thật (<see cref="IReservationDecisionStrategy"/>) có hành vi do test kịch bản hóa, dùng tiêm lỗi cho pipeline chịu lỗi (FI-01…FI-04, FI-10).
/// Mỗi lần được gọi, strategy đếm lượt (từ 1) rồi chạy <see cref="Behavior"/>; token hủy nhận được là token của pipeline
/// (gộp inner timeout, outer timeout và hủy của caller), nên strategy "biết" khi nào bị cắt. Chỉ dùng trong host test (C-10).
/// </summary>
public sealed class ScriptedStrategy : IReservationDecisionStrategy
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _invocationCount;

    /// <summary>Hành vi mỗi lượt gọi: tham số là số thứ tự lượt (từ 1) và token hủy của pipeline. Có thể đổi giữa các request.</summary>
    public Func<int, CancellationToken, Task<StrategyDecision>> Behavior { get; set; } = (_, _) => Task.FromResult(Approve());

    /// <summary>Chính sách cache của strategy; mặc định tắt để cache không che số lần strategy chạy.</summary>
    public StrategyCachePolicy Policy { get; set; } = StrategyCachePolicy.Disabled;

    /// <summary>Số lượt <see cref="EvaluateAsync"/> đã được gọi.</summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    /// <summary>Hoàn tất khi lượt gọi đầu tiên bắt đầu chạy.</summary>
    public Task Started => _started.Task;

    /// <summary>Hoàn tất khi một lượt gọi nhận thấy token hủy bị kích hoạt (strategy bị cắt từ bên ngoài).</summary>
    public Task CancellationObserved => _cancellationObserved.Task;

    /// <inheritdoc />
    public string StrategyName => "FaultInjectionStrategy";

    /// <inheritdoc />
    public string StrategyVersion => "1.0.0";

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy => Policy;

    /// <summary>Quyết định <c>APPROVED</c> cọc 30% mà các kịch bản "strategy khỏe" trả về.</summary>
    public static StrategyDecision Approve() =>
        StrategyDecision.Approve(0.3m, "FI_RULE_APPROVED", ["Strategy giả của test fault injection chấp thuận."]);

    /// <inheritdoc />
    public async Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _invocationCount);
        _started.TrySetResult();
        try
        {
            return await Behavior(attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _cancellationObserved.TrySetResult();
            throw;
        }
    }
}
