using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.V2.Middlewares;
using Xunit;
using ErrorCodes = Shared.DecisionContractSDK.V2.Common.ErrorCodes;

namespace TADSS.UnitTests.V2.Runtime.Middlewares;

/// <summary>
/// UT-08: <see cref="GlobalExceptionHandler"/> dùng <see cref="ExceptionClassifier"/> để dựng phản hồi
/// RFC 7807 theo bảng map lỗi v2 (spec 4.2.3) — đúng status/code/content-type, không lộ message hạ tầng,
/// <c>Retry-After</c> cho 503/504, <c>errors</c> theo <c>FieldName</c>, client hủy thì không ghi response.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private const string TraceId = "trace-handler-001";
    private const string SecretMessage = "SECRET-internal-detail-10.0.0.5";

    private sealed record LogEntry(LogLevel Level, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, exception));
    }

    private sealed record HandlerResult(DefaultHttpContext Context, bool Handled, JsonElement Body, CapturingLogger<GlobalExceptionHandler> Logger);

    private static async Task<HandlerResult> HandleAsync(Exception exception, bool requestAborted = false)
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var context = new DefaultHttpContext { TraceIdentifier = TraceId };
        context.Request.Path = "/api/v2/decisions";
        using var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        using var cts = new CancellationTokenSource();
        if (requestAborted)
        {
            cts.Cancel();
            context.RequestAborted = cts.Token;
        }

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        if (bodyStream.Length == 0)
        {
            return new HandlerResult(context, handled, default, logger);
        }

        bodyStream.Position = 0;
        using var document = await JsonDocument.ParseAsync(bodyStream);
        return new HandlerResult(context, handled, document.RootElement.Clone(), logger);
    }

    private static PostgresException Postgres(string sqlState) =>
        new("postgres error", "ERROR", "ERROR", sqlState);

    public static TheoryData<Exception, int, string> MappingRows => new()
    {
        { new ContractValidationException(ErrorCodes.ValidationError, "sai định dạng", "items"), 400, ErrorCodes.ValidationError },
        { new ContractValidationException(ErrorCodes.MissingHeader, "thiếu header", "Idempotency-Key"), 400, ErrorCodes.MissingHeader },
        { new DomainInvariantException("ANY_CODE", "vi phạm bất biến", "endTime"), 400, ErrorCodes.DomainInvariantViolation },
        { new IdempotencyConflictException("key-01", "key dùng lại"), 409, ErrorCodes.IdempotencyKeyReuse },
        { new PersistenceUnavailableException("mất kết nối", new TimeoutException()), 503, ErrorCodes.PersistenceUnavailable },
        { new TimeoutRejectedException("vượt deadline"), 504, ErrorCodes.GatewayTimeout },
        { new InvalidOperationException("lỗi nội bộ"), 500, ErrorCodes.InternalError },
    };

    public static TheoryData<Exception> RetryableErrors => new()
    {
        new PersistenceUnavailableException("mất kết nối", new TimeoutException()),
        new DbUpdateException("save failed", Postgres("40001")),
        new DbUpdateException("save failed", Postgres("40P01")),
        new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)),
        new TimeoutRejectedException("vượt deadline"),
    };

    public static TheoryData<Exception> ServerSideErrors => new()
    {
        new InvalidOperationException(SecretMessage),
        new ArgumentException(SecretMessage),
        new NullReferenceException(SecretMessage),
        new DbUpdateException(SecretMessage, Postgres("23505")),
    };

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(MappingRows))]
    public async Task Handle_MappingTable_Theory(Exception exception, int expectedStatus, string expectedCode)
    {
        var result = await HandleAsync(exception);

        result.Handled.Should().BeTrue();
        result.Context.Response.StatusCode.Should().Be(expectedStatus);
        result.Context.Response.ContentType.Should().StartWith("application/problem+json");
        result.Body.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        result.Body.GetProperty("code").GetString().Should().Be(expectedCode);
        result.Body.GetProperty("traceId").GetString().Should().Be(TraceId);
        result.Body.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        result.Body.GetProperty("instance").GetString().Should().Be("/api/v2/decisions");
        result.Body.GetProperty("type").GetString().Should().Be(
            $"https://tadss.smashcourt.vn/errors/{expectedCode.ToLowerInvariant().Replace('_', '-')}");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Handle_ClientAbort_WritesNothing()
    {
        var result = await HandleAsync(new OperationCanceledException("Client disconnected"), requestAborted: true);

        result.Handled.Should().BeTrue();
        result.Body.ValueKind.Should().Be(JsonValueKind.Undefined);
        result.Context.Response.ContentType.Should().BeNull();
        result.Logger.Entries.Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Information);
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(RetryableErrors))]
    public async Task Handle_RetryableErrors_SetRetryAfterHeaderAndExtension(Exception exception)
    {
        var result = await HandleAsync(exception);

        result.Context.Response.StatusCode.Should().BeOneOf(
            StatusCodes.Status503ServiceUnavailable, StatusCodes.Status504GatewayTimeout);
        result.Context.Response.Headers["Retry-After"].ToString().Should().Be("5");
        result.Body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(5);
        result.Body.GetProperty("detail").GetString().Should().NotContain("10.0.0.5");
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(ServerSideErrors))]
    public async Task Handle_ServerSideErrors_HideMessage_Theory(Exception exception)
    {
        var result = await HandleAsync(exception);

        result.Context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        result.Body.GetProperty("code").GetString().Should().Be(ErrorCodes.InternalError);
        result.Body.GetProperty("detail").GetString().Should().NotContain(SecretMessage);
        result.Body.TryGetProperty("retryAfterSeconds", out _).Should().BeFalse();
        result.Context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_FieldNameOnValidationErrors_PopulatesErrorsExtension(bool useContractValidation)
    {
        Exception exception = useContractValidation
            ? new ContractValidationException(ErrorCodes.ValidationError, "msg", "items[0].price")
            : new DomainInvariantException("ANY_CODE", "msg", "items[0].price");

        var result = await HandleAsync(exception);

        var errors = result.Body.GetProperty("errors");
        errors.GetProperty("items[0].price").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("msg");
        result.Body.GetProperty("detail").GetString().Should().Be("msg");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Handle_ValidationErrorWithoutFieldName_OmitsErrorsExtension()
    {
        var result = await HandleAsync(new ContractValidationException(ErrorCodes.ValidationError, "msg"));

        result.Context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.Body.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Handle_ContractValidationMissingHeader_Is400MissingHeader()
    {
        var result = await HandleAsync(
            new ContractValidationException(ErrorCodes.MissingHeader, "thiếu header Idempotency-Key", "Idempotency-Key"));

        result.Context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.Body.GetProperty("code").GetString().Should().Be(ErrorCodes.MissingHeader);
        result.Body.GetProperty("detail").GetString().Should().Be("thiếu header Idempotency-Key");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Handle_PersistenceFailureWrappedInOtherException_Is503()
    {
        // Lỗi hạ tầng bị bọc trong exception khác vẫn phải thành 503 (classifier kiểm tra persistence trước)
        var wrapped = new InvalidOperationException(
            "transient failure", new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)));

        var result = await HandleAsync(wrapped);

        result.Context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        result.Body.GetProperty("code").GetString().Should().Be(ErrorCodes.PersistenceUnavailable);
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(400, LogLevel.Warning, false)]
    [InlineData(409, LogLevel.Warning, false)]
    [InlineData(500, LogLevel.Error, true)]
    [InlineData(503, LogLevel.Error, true)]
    [InlineData(504, LogLevel.Error, true)]
    public async Task Handle_LogLevel_Is4xxWarning5xxErrorWithException_Theory(int status, LogLevel expectedLevel, bool expectException)
    {
        Exception exception = status switch
        {
            400 => new ContractValidationException(ErrorCodes.ValidationError, "msg", "f"),
            409 => new IdempotencyConflictException("key", "msg"),
            500 => new InvalidOperationException("msg"),
            503 => new PersistenceUnavailableException("msg", new TimeoutException()),
            _ => new TimeoutRejectedException("msg"),
        };

        var result = await HandleAsync(exception);

        var entry = result.Logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(expectedLevel);
        (entry.Exception is not null).Should().Be(expectException);
    }
}
