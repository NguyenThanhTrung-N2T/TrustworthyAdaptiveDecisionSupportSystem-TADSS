using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Caching;

/// <summary>
/// Dựng pipeline Polly cho mọi lệnh Redis của cache (A-08): Circuit breaker (ngoài) bọc Timeout (trong) nên quá hạn cũng được breaker đếm là lỗi.
/// Breaker mở thì <see cref="BrokenCircuitException"/> được ném ngay, không gọi Redis và không chờ timeout. Chỉ hủy của chính caller
/// không được tính là lỗi; payload hỏng không đi qua pipeline này nên cũng không tính.
/// </summary>
internal static class RedisCacheResilience
{
    /// <summary>
    /// Dựng pipeline từ tham số <c>Tadss:Cache</c>; <paramref name="timeProvider"/> điều khiển cả timeout lẫn breaker;
    /// <paramref name="stateProvider"/> cho phép đọc trạng thái breaker (gauge <c>tadss.cache.circuit.state</c>).
    /// </summary>
    public static ResiliencePipeline Create(CacheOptions options, TimeProvider timeProvider, ILogger logger, CircuitBreakerStateProvider stateProvider) =>
        new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.RedisBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(options.RedisBreakerSamplingSeconds),
                MinimumThroughput = options.RedisBreakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(options.RedisBreakerBreakDurationSeconds),
                StateProvider = stateProvider,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is { } exception
                    && !(exception is OperationCanceledException && args.Context.CancellationToken.IsCancellationRequested)),
                OnOpened = args =>
                {
                    logger.LogError(
                        "Circuit breaker Redis MỞ trong {BreakDuration}s do tỷ lệ lỗi vượt ngưỡng; cache bỏ qua Redis.",
                        args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuit breaker Redis ĐÓNG: cache dùng Redis trở lại.");
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    logger.LogInformation("Circuit breaker Redis NỬA MỞ: đang thăm dò sức khỏe Redis.");
                    return default;
                },
            })
            .AddTimeout(new TimeoutStrategyOptions { Timeout = TimeSpan.FromMilliseconds(options.RedisTimeoutMs) })
            .Build();
}
