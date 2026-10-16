using FluentAssertions;
using TADSS.Application.Options;
using Xunit;

namespace TADSS.UnitTests.Application.Options;

/// <summary>
/// UT-11: <see cref="WarmupOptionsValidator"/> chặn cấu hình làm nóng vô lý (số kết nối, số vòng, ngân sách thời gian ngoài khoảng) lúc khởi động;
/// mặc định hợp lệ.
/// </summary>
public class WarmupOptionsValidatorTests
{
    private static readonly WarmupOptionsValidator Validator = new();

    /// <summary>Giá trị mặc định qua validator.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Validate_Defaults_Succeeds()
    {
        Validator.Validate(null, new WarmupOptions()).Succeeded.Should().BeTrue();
    }

    /// <summary>Tắt làm nóng thì 0 kết nối vẫn hợp lệ; biên dưới/trên của từng tham số.</summary>
    [Theory]
    [Trait("CatalogId", "UT-11")]
    [InlineData(0, 1, WarmupOptionsValidator.MinTimeoutMs, true)]
    [InlineData(WarmupOptionsValidator.MaxDatabaseConnections, WarmupOptionsValidator.MaxIterations, WarmupOptionsValidator.MaxTimeoutMs, true)]
    [InlineData(-1, 1, WarmupOptionsValidator.MinTimeoutMs, false)]
    [InlineData(WarmupOptionsValidator.MaxDatabaseConnections + 1, 1, WarmupOptionsValidator.MinTimeoutMs, false)]
    [InlineData(1, 0, WarmupOptionsValidator.MinTimeoutMs, false)]
    [InlineData(1, WarmupOptionsValidator.MaxIterations + 1, WarmupOptionsValidator.MinTimeoutMs, false)]
    [InlineData(1, 1, WarmupOptionsValidator.MinTimeoutMs - 1, false)]
    [InlineData(1, 1, WarmupOptionsValidator.MaxTimeoutMs + 1, false)]
    public void Validate_Bounds_MatchExpected(int connections, int iterations, int timeoutMs, bool expected)
    {
        var options = new WarmupOptions { DatabaseConnections = connections, Iterations = iterations, TimeoutMs = timeoutMs };

        Validator.Validate(null, options).Succeeded.Should().Be(expected);
    }

    /// <summary>Thông báo lỗi nêu đúng khóa cấu hình đầy đủ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Validate_InvalidIterations_NamesFullConfigurationKey()
    {
        var result = Validator.Validate(null, new WarmupOptions { Iterations = 0 });

        result.Failures.Should().ContainSingle().Which.Should().Contain("Tadss:Warmup:Iterations");
    }
}
