using Shared.DecisionContractSDK.Decisions;

namespace TADSS.Infrastructure.Caching;

/// <summary>Kết quả đọc Redis: trạng thái và quyết định (chỉ có khi <see cref="RedisCacheStatus.Hit"/>).</summary>
/// <param name="Status">Trạng thái thao tác.</param>
/// <param name="Decision">Quyết định đã dựng lại; <c>null</c> khi không phải <see cref="RedisCacheStatus.Hit"/>.</param>
internal sealed record RedisReadResult(RedisCacheStatus Status, StrategyDecision? Decision);
