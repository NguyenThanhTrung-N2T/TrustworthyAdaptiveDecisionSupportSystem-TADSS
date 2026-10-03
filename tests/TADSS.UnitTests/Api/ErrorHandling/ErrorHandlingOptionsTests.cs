using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.ErrorHandling;

/// <summary>
/// UT-08: <c>Tadss:ErrorHandling:RetryAfterSeconds</c> được nối trong <c>AddPresentation</c> với <c>ValidateOnStart</c> —
/// giá trị ngoài khoảng 1..3600 chặn khởi động host, giá trị trong khoảng (kể cả hai biên) đi qua.
/// </summary>
public class ErrorHandlingOptionsTests
{
    private static WebApplicationFactory<Program> FactoryWith(DecisionsApiFactory baseFactory, string retryAfterSeconds) =>
        baseFactory.WithWebHostBuilder(builder =>
            builder.UseSetting($"{ErrorHandlingOptions.SectionName}:{nameof(ErrorHandlingOptions.RetryAfterSeconds)}", retryAfterSeconds));

    /// <summary>0, số âm và 3601 bị từ chối khi khởi động, thông báo nêu khóa cấu hình.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3601")]
    public void ErrorHandlingOptions_InvalidRetryAfter_FailsOnStart_Theory(string retryAfterSeconds)
    {
        using var baseFactory = new DecisionsApiFactory();
        using var factory = FactoryWith(baseFactory, retryAfterSeconds);

        var start = () => factory.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage("*Tadss:ErrorHandling:RetryAfterSeconds*");
    }

    /// <summary>Hai biên 1 và 3600 hợp lệ và được bind vào options.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("1", 1)]
    [InlineData("3600", 3600)]
    public void ErrorHandlingOptions_BoundaryRetryAfter_StartsHost_Theory(string retryAfterSeconds, int expected)
    {
        using var baseFactory = new DecisionsApiFactory();
        using var factory = FactoryWith(baseFactory, retryAfterSeconds);

        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<ErrorHandlingOptions>>().Value.RetryAfterSeconds.Should().Be(expected);
    }

    /// <summary>Validator đứng riêng: chỉ <c>1 ≤ RetryAfterSeconds ≤ 3600</c> là hợp lệ.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void Validate_RetryAfterRange_Theory(int value, bool expectedValid)
    {
        var result = new ErrorHandlingOptionsValidator().Validate(null, new ErrorHandlingOptions { RetryAfterSeconds = value });

        result.Succeeded.Should().Be(expectedValid);
    }
}
