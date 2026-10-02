using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Infrastructure.Resilience;

namespace TADSS.Runtime.Api.V2.Infrastructure.Resilience;

/// <summary>
/// Kiểm tra <see cref="ResilienceOptions"/> khi khởi động (fail-fast): mọi giá trị phải dương
/// (<c>MaxRetryAttempts ≥ 0</c>), <c>0 &lt; CircuitBreakerFailureRatio ≤ 1</c> và ngân sách thời gian của pipeline
/// <c>InnerTimeoutMs × (MaxRetryAttempts + 1) + RetryBaseDelayMs × MaxRetryAttempts ≤ OuterTimeoutMs</c>.
/// Section <c>Tadss:Resilience</c> dùng chung với v1 nên kiểm tra này áp dụng cho cả hai.
/// </summary>
public sealed class ResilienceOptionsValidator : IValidateOptions<ResilienceOptions>
{
    /// <summary>Số lần gọi tối thiểu Polly yêu cầu để circuit breaker lấy mẫu.</summary>
    private const int MinimumBreakerThroughput = 2;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ResilienceOptions options)
    {
        var failures = new List<string>();
        var section = ResilienceOptions.SectionName;

        RequirePositive(failures, section, nameof(options.OuterTimeoutMs), options.OuterTimeoutMs);
        RequirePositive(failures, section, nameof(options.InnerTimeoutMs), options.InnerTimeoutMs);
        RequirePositive(failures, section, nameof(options.RetryBaseDelayMs), options.RetryBaseDelayMs);
        RequirePositive(failures, section, nameof(options.CircuitBreakerSamplingSeconds), options.CircuitBreakerSamplingSeconds);
        RequirePositive(failures, section, nameof(options.CircuitBreakerBreakDurationSeconds), options.CircuitBreakerBreakDurationSeconds);

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add($"{section}:{nameof(options.MaxRetryAttempts)} không được âm (hiện là {options.MaxRetryAttempts}).");
        }

        if (options.CircuitBreakerMinimumThroughput < MinimumBreakerThroughput)
        {
            failures.Add(
                $"{section}:{nameof(options.CircuitBreakerMinimumThroughput)} phải từ {MinimumBreakerThroughput} trở lên " +
                $"(hiện là {options.CircuitBreakerMinimumThroughput}).");
        }

        // Viết dạng phủ định để NaN cũng bị từ chối
        if (!(options.CircuitBreakerFailureRatio > 0d && options.CircuitBreakerFailureRatio <= 1d))
        {
            failures.Add(
                $"{section}:{nameof(options.CircuitBreakerFailureRatio)} phải nằm trong khoảng (0, 1] " +
                $"(hiện là {options.CircuitBreakerFailureRatio}).");
        }

        if (failures.Count == 0)
        {
            // Dùng long để tránh tràn số khi cấu hình có giá trị cực lớn
            var attempts = (long)options.MaxRetryAttempts + 1;
            var budget = (long)options.InnerTimeoutMs * attempts + (long)options.RetryBaseDelayMs * options.MaxRetryAttempts;
            if (budget > options.OuterTimeoutMs)
            {
                failures.Add(
                    $"{section} vượt ngân sách thời gian: InnerTimeoutMs ({options.InnerTimeoutMs}) × (MaxRetryAttempts ({options.MaxRetryAttempts}) + 1) " +
                    $"+ RetryBaseDelayMs ({options.RetryBaseDelayMs}) × MaxRetryAttempts = {budget} ms, lớn hơn OuterTimeoutMs ({options.OuterTimeoutMs} ms).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, string section, string property, int value)
    {
        if (value <= 0)
        {
            failures.Add($"{section}:{property} phải lớn hơn 0 (hiện là {value}).");
        }
    }
}
