using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.V2.Middlewares;
using Xunit;
using V1ErrorCodes = Shared.DecisionContractSDK.Common.ErrorCodes;
using V2ErrorCodes = Shared.DecisionContractSDK.V2.Common.ErrorCodes;

namespace TADSS.UnitTests.V2.Runtime.Middlewares;

/// <summary>
/// UT-08: các thay đổi bổ sung của <see cref="GlobalExceptionHandler"/> ở gói 5.2b — mở rộng 503 cho
/// <c>PersistenceUnavailableException</c> và SqlState 40001/40P01, thêm header <c>Retry-After: 5</c>; mã v1 giữ nguyên.
/// </summary>
public class GlobalExceptionHandlerAdditionsTests
{
    public static TheoryData<Exception, int, string> ExistingV1Mappings => new()
    {
        { new InvalidOperationException("sai thao tác"), StatusCodes.Status400BadRequest, V1ErrorCodes.BadRequest },
        { new DomainInvariantException("INV_CODE", "vi phạm", "field"), StatusCodes.Status400BadRequest, "INV_CODE" },
    };

    private static async Task<(DefaultHttpContext Context, JsonElement Body)> HandleAsync(Exception exception)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext { TraceIdentifier = "trace-additions-001" };
        context.Request.Path = "/api/v1/decisions";
        using var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        handled.Should().BeTrue();

        bodyStream.Position = 0;
        using var document = await JsonDocument.ParseAsync(bodyStream);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Handler_PersistenceUnavailable_SetsRetryAfterHeader()
    {
        var (context, body) = await HandleAsync(
            new PersistenceUnavailableException("mất kết nối", new TimeoutException()));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers["Retry-After"].ToString().Should().Be("5");
        body.GetProperty("code").GetString().Should().Be(V2ErrorCodes.PersistenceUnavailable);
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(5);
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("40P01")]
    [InlineData("40001")]
    public async Task Handler_DeadlockSqlState_Returns503_Theory(string sqlState)
    {
        var (context, body) = await HandleAsync(new PostgresException("deadlock detected", "ERROR", "ERROR", sqlState));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers["Retry-After"].ToString().Should().Be("5");
        body.GetProperty("code").GetString().Should().Be(V2ErrorCodes.PersistenceUnavailable);
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(ExistingV1Mappings))]
    public async Task Handler_ExistingV1Mappings_Unchanged_Theory(Exception exception, int expectedStatus, string expectedCode)
    {
        var (context, body) = await HandleAsync(exception);

        context.Response.StatusCode.Should().Be(expectedStatus);
        body.GetProperty("code").GetString().Should().Be(expectedCode);
        context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }
}
