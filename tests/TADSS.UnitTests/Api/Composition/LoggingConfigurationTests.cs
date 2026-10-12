using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-08: middleware <c>ExceptionHandlerMiddleware</c> của ASP.NET Core luôn log Error trước khi gọi <c>GlobalExceptionHandler</c>, kể cả với lỗi 4xx (404 không tìm thấy quyết định);
/// cấu hình gốc hạ category đó xuống Fatal để chỉ còn dòng log của <c>GlobalExceptionHandler</c> với đúng mức (5xx Error, 4xx Warning).
/// </summary>
public class LoggingConfigurationTests
{
    private const string ExceptionHandlerMiddlewareCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    /// <summary><c>appsettings.json</c> đặt override Serilog <c>Fatal</c> cho đúng category của middleware.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void AppSettings_SilencesExceptionHandlerMiddlewareCategory()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();

        configuration[$"Serilog:MinimumLevel:Override:{ExceptionHandlerMiddlewareCategory}"].Should().Be("Fatal");
    }
}
