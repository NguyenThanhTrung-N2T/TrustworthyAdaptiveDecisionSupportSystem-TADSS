using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Strategy v2 giả viết tay: hành vi do delegate quyết định, đếm số lần được gọi (thread-safe) và ghi lại tham số
/// của lần gọi cuối. Dùng chung cho test của các thành phần runtime v2 cần một strategy có hành vi điều khiển được.
/// </summary>
internal sealed class DelegateStrategy : IReservationDecisionStrategy
{
    private readonly Func<Reservation, DecisionContext, CancellationToken, Task<StrategyDecision>> _behavior;
    private readonly StrategyCachePolicy _cachePolicy;
    private int _invocationCount;

    public DelegateStrategy(
        Func<Reservation, DecisionContext, CancellationToken, Task<StrategyDecision>> behavior,
        StrategyCachePolicy? cachePolicy = null)
    {
        _behavior = behavior;
        _cachePolicy = cachePolicy ?? StrategyCachePolicy.Disabled;
    }

    public string StrategyName => "DelegateStrategy";

    public string StrategyVersion => "2.0.0";

    public StrategyCachePolicy CachePolicy => _cachePolicy;

    /// <summary>Số lần <see cref="EvaluateAsync"/> đã được gọi.</summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    /// <summary>Reservation của lần gọi cuối.</summary>
    public Reservation? LastReservation { get; private set; }

    /// <summary>Context của lần gọi cuối.</summary>
    public DecisionContext? LastContext { get; private set; }

    public Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);
        LastReservation = reservation;
        LastContext = context;
        return _behavior(reservation, context, cancellationToken);
    }
}
