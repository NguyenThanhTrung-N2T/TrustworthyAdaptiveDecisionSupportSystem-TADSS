using FluentAssertions;
using TADSS.Infrastructure.Options;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Options;

/// <summary>
/// UT-09: <see cref="FaultInjectionOptionsValidator"/> chặn cấu hình tiêm lỗi sai ngay lúc khởi động (kể cả khi <c>Enabled = false</c>,
/// vì options luôn đăng ký): <c>DelayMs</c> phải trong 1..60000 và <c>TargetStrategy</c> nếu có không được chỉ gồm khoảng trắng.
/// </summary>
public class FaultInjectionOptionsValidatorTests
{
    private static readonly FaultInjectionOptionsValidator Validator = new();

    /// <summary>Hai ca biên (0 và 60001) và <c>TargetStrategy</c> toàn khoảng trắng bị từ chối, message nêu khóa; 1000, 1, 60000 và <c>null</c> đạt.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(0, null, false, "DelayMs")]
    [InlineData(-5, null, false, "DelayMs")]
    [InlineData(60001, null, false, "DelayMs")]
    [InlineData(1000, "  ", false, "TargetStrategy")]
    [InlineData(1, null, true, null)]
    [InlineData(60000, null, true, null)]
    [InlineData(1000, null, true, null)]
    [InlineData(1000, "MockAlternativeDecisionStrategy", true, null)]
    public void Validate_Theory(int delayMs, string? targetStrategy, bool expectedValid, string? expectedKey)
    {
        var options = new FaultInjectionOptions { DelayMs = delayMs, TargetStrategy = targetStrategy };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().Be(expectedValid);
        if (expectedKey is not null)
        {
            result.FailureMessage.Should().Contain($"{FaultInjectionOptions.SectionName}:{expectedKey}");
        }
    }

    /// <summary>Mặc định của options (tắt, <c>Profile = None</c>, <c>DelayMs = 1000</c>, không chỉ định strategy) hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Validate_Defaults_AreValidAndDisabled()
    {
        var options = new FaultInjectionOptions();

        Validator.Validate(null, options).Succeeded.Should().BeTrue();
        options.Enabled.Should().BeFalse();
        options.Profile.Should().Be(FaultInjectionProfile.None);
        options.DelayMs.Should().Be(1000);
        options.TargetStrategy.Should().BeNull();
    }
}
