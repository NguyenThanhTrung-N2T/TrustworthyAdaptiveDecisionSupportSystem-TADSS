using FluentAssertions;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Resilience;

/// <summary>
/// UT-07 (phần cấu hình): <see cref="ResilienceOptionsValidator"/> chặn cấu hình pipeline sai ngay lúc khởi động —
/// giá trị không dương, tỷ lệ lỗi ngoài (0, 1] và ngân sách thời gian vượt Outer timeout
/// (<c>Inner × (Retry+1) + Delay × Retry ≤ Outer</c>).
/// </summary>
public class ResilienceOptionsValidatorTests
{
    private static readonly ResilienceOptionsValidator Validator = new();

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = Validator.Validate(null, new ResilienceOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Validate_BudgetExceedsOuter_Fails()
    {
        var options = new ResilienceOptions
        {
            OuterTimeoutMs = 500,
            InnerTimeoutMs = 200,
            MaxRetryAttempts = 2,
            RetryBaseDelayMs = 30
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Contain("200").And.Contain("660").And.Contain("500")
            .And.Contain("InnerTimeoutMs").And.Contain("OuterTimeoutMs");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Validate_BudgetExactlyEqualsOuter_Succeeds()
    {
        var options = new ResilienceOptions
        {
            OuterTimeoutMs = 420,
            InnerTimeoutMs = 120,
            MaxRetryAttempts = 2,
            RetryBaseDelayMs = 30
        };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Validate_ZeroRetries_Succeeds()
    {
        var options = new ResilienceOptions { MaxRetryAttempts = 0 };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Validate_ExtremeValues_DoesNotOverflow()
    {
        var options = new ResilienceOptions
        {
            OuterTimeoutMs = int.MaxValue,
            InnerTimeoutMs = int.MaxValue,
            MaxRetryAttempts = 1000,
            RetryBaseDelayMs = int.MaxValue
        };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    public static TheoryData<string, Action<ResilienceOptions>> InvalidMutations => new()
    {
        { nameof(ResilienceOptions.OuterTimeoutMs) + "=0", o => o.OuterTimeoutMs = 0 },
        { nameof(ResilienceOptions.OuterTimeoutMs) + "=-1", o => o.OuterTimeoutMs = -1 },
        { nameof(ResilienceOptions.InnerTimeoutMs) + "=0", o => o.InnerTimeoutMs = 0 },
        { nameof(ResilienceOptions.InnerTimeoutMs) + "=-5", o => o.InnerTimeoutMs = -5 },
        { nameof(ResilienceOptions.MaxRetryAttempts) + "=-1", o => o.MaxRetryAttempts = -1 },
        { nameof(ResilienceOptions.RetryBaseDelayMs) + "=0", o => o.RetryBaseDelayMs = 0 },
        { nameof(ResilienceOptions.RetryBaseDelayMs) + "=-1", o => o.RetryBaseDelayMs = -1 },
        { nameof(ResilienceOptions.CircuitBreakerSamplingSeconds) + "=0", o => o.CircuitBreakerSamplingSeconds = 0 },
        { nameof(ResilienceOptions.CircuitBreakerFailureRatio) + "=0", o => o.CircuitBreakerFailureRatio = 0 },
        { nameof(ResilienceOptions.CircuitBreakerFailureRatio) + "=-0.1", o => o.CircuitBreakerFailureRatio = -0.1 },
        { nameof(ResilienceOptions.CircuitBreakerFailureRatio) + "=1.01", o => o.CircuitBreakerFailureRatio = 1.01 },
        { nameof(ResilienceOptions.CircuitBreakerFailureRatio) + "=NaN", o => o.CircuitBreakerFailureRatio = double.NaN },
        { nameof(ResilienceOptions.CircuitBreakerMinimumThroughput) + "=0", o => o.CircuitBreakerMinimumThroughput = 0 },
        { nameof(ResilienceOptions.CircuitBreakerMinimumThroughput) + "=1", o => o.CircuitBreakerMinimumThroughput = 1 },
        { nameof(ResilienceOptions.CircuitBreakerBreakDurationSeconds) + "=0", o => o.CircuitBreakerBreakDurationSeconds = 0 },
    };

    [Theory]
    [Trait("CatalogId", "UT-07")]
    [MemberData(nameof(InvalidMutations))]
    public void Validate_NonPositiveValues_Fail_Theory(string description, Action<ResilienceOptions> mutate)
    {
        var options = new ResilienceOptions();
        mutate(options);

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue($"cấu hình {description} không hợp lệ");
        result.Failures.Should().NotBeEmpty();
    }

    [Theory]
    [Trait("CatalogId", "UT-07")]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_FailureRatioInRange_Succeeds_Theory(double ratio)
    {
        var options = new ResilienceOptions { CircuitBreakerFailureRatio = ratio };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }
}
