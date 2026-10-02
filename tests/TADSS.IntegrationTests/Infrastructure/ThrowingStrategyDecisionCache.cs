using Shared.DecisionContractSDK.Decisions;
using TADSS.Runtime.Api.Infrastructure.Caching;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Cache hỏng chỉ dùng trong host test (C-10): cả <see cref="GetAsync"/> lẫn <see cref="SetAsync"/> đều ném exception, mô phỏng Redis mất kết nối
/// ở mức dịch vụ (FI-06, phần cô lập lỗi). Đếm số lần được gọi để test chứng minh đường cache thực sự đã chạy.
/// </summary>
public sealed class ThrowingStrategyDecisionCache : IStrategyDecisionCache
{
    private int _getCalls;
    private int _setCalls;

    /// <summary>Số lần <see cref="GetAsync"/> được gọi.</summary>
    public int GetCalls => Volatile.Read(ref _getCalls);

    /// <summary>Số lần <see cref="SetAsync"/> được gọi.</summary>
    public int SetCalls => Volatile.Read(ref _setCalls);

    /// <inheritdoc />
    public Task<StrategyDecision?> GetAsync(string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _getCalls);
        throw new InvalidOperationException("Redis connection refused / cluster down.");
    }

    /// <inheritdoc />
    public Task SetAsync(string key, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _setCalls);
        throw new InvalidOperationException("Redis write timeout / cluster down.");
    }
}
