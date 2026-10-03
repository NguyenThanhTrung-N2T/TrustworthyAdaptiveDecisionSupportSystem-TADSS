using TADSS.Application.Abstractions.Resilience;

namespace TADSS.Application.Decisions;

/// <summary>
/// Kết quả của <see cref="StrategyEvaluator"/>: kết quả chạy strategy (từ cache hoặc từ pipeline) và cờ cho biết có đến từ cache hay không.
/// </summary>
/// <param name="Outcome">Kết quả thực thi: <c>CACHE_HIT</c> khi đọc từ cache, ngược lại là kết quả của pipeline (<c>AUTOMATED</c>/<c>FALLBACK</c>).</param>
/// <param name="CacheHit">Có dùng kết quả đọc từ cache hay không.</param>
public sealed record StrategyEvaluation(StrategyExecutionOutcome Outcome, bool CacheHit);
