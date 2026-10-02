using Shared.DecisionContractSDK.Decisions;

namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Cache kết quả của strategy (<see cref="StrategyDecision"/>), không cache response (V-3): response chứa
/// <c>decisionId</c>, thời gian và approval window nên mỗi lần đánh giá phải được dựng mới.
/// </summary>
public interface IStrategyDecisionCache
{
    /// <summary>
    /// Đọc quyết định đã cache. Mọi lỗi cache (Redis, memory, payload hỏng) được coi là miss.
    /// </summary>
    /// <param name="key">Khóa do <see cref="StrategyDecisionCacheKey.Build"/> dựng.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Quyết định đã cache, hoặc <c>null</c> khi miss.</returns>
    /// <exception cref="OperationCanceledException">Khi chính <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<StrategyDecision?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Ghi quyết định vào cache. Mọi lỗi cache được bỏ qua (chỉ ghi log).
    /// </summary>
    /// <param name="key">Khóa do <see cref="StrategyDecisionCacheKey.Build"/> dựng.</param>
    /// <param name="decision">Quyết định của strategy cần cache.</param>
    /// <param name="ttl">Thời gian sống (do <c>StrategyCachePolicy.Ttl</c> quyết định), phải dương.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <exception cref="OperationCanceledException">Khi chính <paramref name="cancellationToken"/> bị hủy.</exception>
    Task SetAsync(string key, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken);
}
