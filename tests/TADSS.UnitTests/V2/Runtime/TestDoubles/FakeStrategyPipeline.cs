using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;

namespace TADSS.UnitTests.V2.Runtime.TestDoubles;

/// <summary>
/// <see cref="IStrategyPipeline"/> giả viết tay: đếm số lần được gọi, ghi lại tham số lần gọi cuối và để test chọn kết quả
/// (mặc định chạy thẳng strategy được truyền vào rồi bọc thành <c>AUTOMATED</c>, không có Polly). Dùng để kiểm tra
/// <c>DecisionService</c> mà không phụ thuộc pipeline thật.
/// </summary>
internal sealed class FakeStrategyPipeline : IStrategyPipeline
{
    private readonly Func<IReservationDecisionStrategy, Reservation, DecisionContext, CancellationToken, Task<StrategyExecutionOutcome>> _behavior;
    private int _invocationCount;

    /// <summary>Pipeline mặc định: chạy strategy thật và trả <c>AUTOMATED</c> với thời gian 0 ms.</summary>
    public FakeStrategyPipeline()
        : this(RunStrategyAsync)
    {
    }

    /// <summary>Pipeline với hành vi do test quyết định.</summary>
    public FakeStrategyPipeline(
        Func<IReservationDecisionStrategy, Reservation, DecisionContext, CancellationToken, Task<StrategyExecutionOutcome>> behavior)
    {
        _behavior = behavior;
    }

    /// <summary>Số lần <see cref="ExecuteAsync"/> đã được gọi.</summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    /// <summary>Context của lần gọi cuối.</summary>
    public DecisionContext? LastContext { get; private set; }

    /// <summary>Pipeline luôn trả kết quả <c>FALLBACK</c> cố định.</summary>
    public static FakeStrategyPipeline Fallback(StrategyDecision decision, string failureReason, int executionTimeMs = 412) =>
        new((_, _, _, _) => Task.FromResult(
            new StrategyExecutionOutcome(decision, ExecutionType.FALLBACK, FallbackUsed: true, failureReason, executionTimeMs)));

    public Task<StrategyExecutionOutcome> ExecuteAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        DecisionContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);
        LastContext = context;
        return _behavior(strategy, reservation, context, cancellationToken);
    }

    private static async Task<StrategyExecutionOutcome> RunStrategyAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        DecisionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = await strategy.EvaluateAsync(reservation, context, cancellationToken);
        return new StrategyExecutionOutcome(decision, ExecutionType.AUTOMATED, FallbackUsed: false, FailureReason: null, ExecutionTimeMs: 0);
    }
}
