using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Double của strategy chỉ dùng trong host test (C-10): bọc strategy thật, đếm số lần được gọi và cho phép test ghi đè
/// <see cref="CachePolicy"/> (ví dụ <see cref="StrategyCachePolicy.Disabled"/>). Tên, phiên bản và quyết định giữ nguyên của strategy thật
/// nên khóa cache, response và audit không đổi so với chạy thật.
/// </summary>
public sealed class ProbeStrategy : IReservationDecisionStrategy
{
    private readonly IReservationDecisionStrategy _inner;
    private readonly ProbeStrategyRegistry _owner;

    /// <summary>Bọc <paramref name="inner"/>; số lần gọi và chính sách cache ghi đè nằm ở <paramref name="owner"/>.</summary>
    public ProbeStrategy(IReservationDecisionStrategy inner, ProbeStrategyRegistry owner)
    {
        _inner = inner;
        _owner = owner;
    }

    /// <inheritdoc />
    public string StrategyName => _inner.StrategyName;

    /// <inheritdoc />
    public string StrategyVersion => _inner.StrategyVersion;

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy => _owner.CachePolicyOverride ?? _inner.CachePolicy;

    /// <inheritdoc />
    public Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        _owner.RecordInvocation();
        return _inner.EvaluateAsync(reservation, context, cancellationToken);
    }
}
