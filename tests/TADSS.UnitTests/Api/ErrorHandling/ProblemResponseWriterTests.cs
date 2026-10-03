using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TADSS.Runtime.Api.ErrorHandling;
using Xunit;

namespace TADSS.UnitTests.Api.ErrorHandling;

/// <summary>
/// UT-08: <see cref="ProblemResponseWriter"/> là đường duy nhất dựng body problem+json (spec 4.2.4); các trường tùy chọn
/// (<c>retryAfterSeconds</c>, header <c>Retry-After</c>, <c>errors</c>) chỉ xuất hiện khi <see cref="ProblemResponse"/> có giá trị.
/// </summary>
public class ProblemResponseWriterTests
{
    private const string TraceId = "trace-writer-001";

    private static readonly string[] BaseFields = ["type", "title", "status", "detail", "instance", "code", "traceId"];

    private static async Task<(DefaultHttpContext Context, JsonElement Body)> WriteAsync(ProblemResponse problem)
    {
        var context = new DefaultHttpContext { TraceIdentifier = TraceId };
        context.Request.Path = "/api/v2/decisions";
        using var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        await ProblemResponseWriter.WriteAsync(context, problem, CancellationToken.None);

        bodyStream.Position = 0;
        using var document = await JsonDocument.ParseAsync(bodyStream);
        return (context, document.RootElement.Clone());
    }

    /// <summary>Có và không có <c>RetryAfterSeconds</c>, <c>Errors</c>: đúng tập trường, <c>Retry-After</c> chỉ khi có.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WriteAsync_ShapesProblem_Theory(bool withRetryAfter, bool withErrors)
    {
        var errors = new Dictionary<string, string[]> { ["reason"] = ["Quá ngắn."] };
        var problem = new ProblemResponse(
            StatusCodes.Status503ServiceUnavailable,
            "PERSISTENCE_UNAVAILABLE",
            "Persistence Unavailable",
            "Chi tiết chung.",
            withRetryAfter ? 7 : null,
            withErrors ? errors : null);

        var (context, body) = await WriteAsync(problem);

        context.Response.StatusCode.Should().Be(503);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        var expectedFields = BaseFields.AsEnumerable();
        if (withRetryAfter)
        {
            expectedFields = expectedFields.Append("retryAfterSeconds");
        }

        if (withErrors)
        {
            expectedFields = expectedFields.Append("errors");
        }

        body.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(expectedFields);
        body.GetProperty("type").GetString().Should().Be("https://tadss.smashcourt.vn/errors/persistence-unavailable");
        body.GetProperty("title").GetString().Should().Be("Persistence Unavailable");
        body.GetProperty("status").GetInt32().Should().Be(503);
        body.GetProperty("detail").GetString().Should().Be("Chi tiết chung.");
        body.GetProperty("instance").GetString().Should().Be("/api/v2/decisions");
        body.GetProperty("code").GetString().Should().Be("PERSISTENCE_UNAVAILABLE");
        body.GetProperty("traceId").GetString().Should().Be(TraceId);
        if (withRetryAfter)
        {
            body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(7);
            context.Response.Headers.RetryAfter.ToString().Should().Be("7");
        }
        else
        {
            context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
        }

        if (withErrors)
        {
            body.GetProperty("errors").GetProperty("reason")[0].GetString().Should().Be("Quá ngắn.");
        }
    }
}
