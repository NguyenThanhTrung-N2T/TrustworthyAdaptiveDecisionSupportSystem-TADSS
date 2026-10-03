using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Middlewares;
using Xunit;

namespace TADSS.UnitTests.Middlewares;

/// <summary>
/// UT-08: đường cơ bản của <see cref="CorrelationIdMiddleware"/> — giữ nguyên correlation id hợp lệ trong
/// <c>TraceIdentifier</c> và sinh GUID mới khi header thiếu hoặc chỉ gồm khoảng trắng. Các trường hợp biên
/// (giá trị quá dài, log scope, response header) nằm ở <see cref="CorrelationIdMiddlewareTests"/>.
/// </summary>
public class CorrelationIdMiddlewareBasicTests
{
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task InvokeAsync_WhenHeaderPresent_PreservesCorrelationIdInTraceIdentifier()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers[HttpHeaderNames.CorrelationId] = "CORR-CUSTOM-12345";
        string? capturedTraceId = null;

        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            capturedTraceId = ctx.TraceIdentifier;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedTraceId.Should().Be("CORR-CUSTOM-12345");
        context.TraceIdentifier.Should().Be("CORR-CUSTOM-12345");
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_WhenHeaderMissingOrWhitespace_GeneratesValidGuid(string? rawHeader)
    {
        // Arrange
        var context = new DefaultHttpContext();
        if (rawHeader != null)
        {
            context.Request.Headers[HttpHeaderNames.CorrelationId] = rawHeader;
        }

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Guid.TryParse(context.TraceIdentifier, out var parsedGuid).Should().BeTrue();
        parsedGuid.Should().NotBeEmpty();
    }
}
