using FluentAssertions;
using TADSS.Application.Options;
using Xunit;

namespace TADSS.UnitTests.Application.Options;

/// <summary>
/// IT-12 (A-11): <see cref="ShadowOptionsValidator"/> chặn cấu hình <c>Tadss:Shadow</c> sai — bật mà thiếu <c>Strategy</c>, <c>SampleRate</c> ngoài [0, 1],
/// <c>QueueCapacity</c> ngoài 1..10000 — với thông báo nêu đúng đường dẫn khóa cấu hình.
/// </summary>
public class ShadowOptionsValidatorTests
{
    private const string Mock = "MockAlternativeDecisionStrategy";

    private static ShadowOptions Scenario(string scenario) => scenario switch
    {
        "enabled-without-strategy" => new() { Enabled = true, Strategy = "  " },
        "sample-rate-negative" => new() { SampleRate = -0.1 },
        "sample-rate-above-one" => new() { SampleRate = 1.1 },
        "sample-rate-nan" => new() { SampleRate = double.NaN },
        "queue-zero" => new() { QueueCapacity = 0 },
        "queue-above-max" => new() { QueueCapacity = 10_001 },
        "disabled-defaults" => new(),
        "enabled-valid" => new() { Enabled = true, Strategy = Mock, SampleRate = 0, QueueCapacity = 10_000 },
        "boundaries" => new() { Enabled = true, Strategy = Mock, SampleRate = 1, QueueCapacity = 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
    };

    /// <summary>Sáu ca sai đều fail và nêu khóa sai; mặc định tắt và các ca hợp lệ (kể cả biên) đạt.</summary>
    [Theory]
    [Trait("CatalogId", "IT-12")]
    [InlineData("enabled-without-strategy", "Tadss:Shadow:Strategy")]
    [InlineData("sample-rate-negative", "Tadss:Shadow:SampleRate")]
    [InlineData("sample-rate-above-one", "Tadss:Shadow:SampleRate")]
    [InlineData("sample-rate-nan", "Tadss:Shadow:SampleRate")]
    [InlineData("queue-zero", "Tadss:Shadow:QueueCapacity")]
    [InlineData("queue-above-max", "Tadss:Shadow:QueueCapacity")]
    [InlineData("disabled-defaults", null)]
    [InlineData("enabled-valid", null)]
    [InlineData("boundaries", null)]
    public void Validate_Theory(string scenario, string? expectedFailureKey)
    {
        var result = new ShadowOptionsValidator().Validate(null, Scenario(scenario));

        if (expectedFailureKey is null)
        {
            result.Succeeded.Should().BeTrue(scenario);
        }
        else
        {
            result.Failed.Should().BeTrue(scenario);
            result.FailureMessage.Should().Contain(expectedFailureKey);
        }
    }

    /// <summary>Mặc định an toàn: tắt, lấy mẫu 100%, hàng đợi 256.</summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public void Defaults_AreDisabledSampleAllQueue256()
    {
        var options = new ShadowOptions();

        options.Enabled.Should().BeFalse();
        options.SampleRate.Should().Be(1.0);
        options.QueueCapacity.Should().Be(256);
        ShadowOptions.SectionName.Should().Be("Tadss:Shadow");
    }
}
