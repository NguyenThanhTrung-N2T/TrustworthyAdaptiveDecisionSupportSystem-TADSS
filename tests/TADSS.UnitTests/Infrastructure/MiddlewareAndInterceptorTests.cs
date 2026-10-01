using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Services;
using Xunit;

namespace TADSS.UnitTests.Infrastructure;

public class MiddlewareAndInterceptorTests
{
    #region 1. CorrelationIdMiddleware Unit Tests

    [Fact]
    public async Task CorrelationIdMiddleware_WhenHeaderPresent_PreservesCorrelationIdInTraceIdentifier()
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
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CorrelationIdMiddleware_WhenHeaderMissingOrWhitespace_GeneratesValidGuid(string? rawHeader)
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

    #endregion

    #region 2. AuditTimestampInterceptor Unit Tests

    private static TadssDbContext CreateDbContextWithInterceptor()
    {
        var interceptor = new AuditTimestampInterceptor();
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        return new TadssDbContext(options);
    }

    private static DecisionExecution CreateSampleEntity()
    {
        return new DecisionExecution
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            IdempotencyKey = $"key-{Guid.NewGuid():N}",
            RequestHash = new string('a', 64),
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            ExecutionType = "AUTOMATED",
            NeedApproval = true,
            ResponsePayload = "{}",
            Version = 1
        };
    }

    [Fact]
    public async Task AuditTimestampInterceptor_SaveChangesAsync_SetsTimestampsOnAddAndIncrementsVersionOnModify()
    {
        // Arrange
        using var dbContext = CreateDbContextWithInterceptor();
        var entity = CreateSampleEntity();
        var beforeAdd = DateTime.UtcNow.AddSeconds(-1);

        // Act 1: Insert (EntityState.Added)
        dbContext.DecisionExecutions.Add(entity);
        await dbContext.SaveChangesAsync();

        // Assert 1: CreatedAt and UpdatedAt set, Version remains 1
        entity.CreatedAt.Should().BeOnOrAfter(beforeAdd);
        entity.UpdatedAt.Should().Be(entity.CreatedAt);
        entity.Version.Should().Be(1);

        var initialCreatedAt = entity.CreatedAt;

        // Act 2: Modify (EntityState.Modified)
        entity.IsOverridden = true;
        entity.OverriddenBy = "manager-01";
        entity.OverrideReason = "Approved after manual verification";
        await dbContext.SaveChangesAsync();

        // Assert 2: CreatedAt preserved, UpdatedAt refreshed, Version incremented to 2
        entity.CreatedAt.Should().Be(initialCreatedAt);
        entity.UpdatedAt.Should().BeOnOrAfter(initialCreatedAt);
        entity.Version.Should().Be(2);
    }

    [Fact]
    public void AuditTimestampInterceptor_SynchronousSaveChanges_AlsoUpdatesTimestampsAndIncrementsVersion()
    {
        // Arrange
        using var dbContext = CreateDbContextWithInterceptor();
        var entity = CreateSampleEntity();

        // Act 1: Synchronous SaveChanges on Add
        dbContext.DecisionExecutions.Add(entity);
        dbContext.SaveChanges();
        entity.Version.Should().Be(1);

        // Act 2: Synchronous SaveChanges on Modify
        entity.IsOverridden = true;
        dbContext.SaveChanges();

        // Assert
        entity.Version.Should().Be(2);
    }

    #endregion

    #region 3. GlobalExceptionHandler Unit Tests

    private static async Task<(int StatusCode, JsonElement Body)> ExecuteExceptionHandlerAsync(
        Exception exception,
        bool cancelRequestAborted = false)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-unit-test-001"
        };
        context.Request.Path = "/api/v1/decisions";
        using var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        using var cts = new CancellationTokenSource();
        if (cancelRequestAborted)
        {
            cts.Cancel();
            context.RequestAborted = cts.Token;
        }

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        handled.Should().BeTrue();

        if (bodyStream.Length == 0)
        {
            return (context.Response.StatusCode, default);
        }

        bodyStream.Position = 0;
        using var doc = await JsonDocument.ParseAsync(bodyStream);
        return (context.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task GlobalExceptionHandler_DbUpdateConcurrencyException_Returns409ConflictWithStandardErrorCode()
    {
        var (statusCode, body) = await ExecuteExceptionHandlerAsync(
            new DbUpdateConcurrencyException("Simulated optimistic concurrency violation"));

        statusCode.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("status").GetInt32().Should().Be(409);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
        body.GetProperty("traceId").GetString().Should().Be("trace-unit-test-001");
    }

    [Fact]
    public async Task GlobalExceptionHandler_IdempotencyConflictException_Returns409IdempotencyKeyReuse()
    {
        var (statusCode, body) = await ExecuteExceptionHandlerAsync(
            new IdempotencyConflictException("key-01", "Key reused with different payload"));

        statusCode.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.IdempotencyKeyReuse);
    }

    [Fact]
    public async Task GlobalExceptionHandler_DomainInvariantException_Returns400WithFieldErrors()
    {
        var ex = new DomainInvariantException(
            ErrorCodes.InvalidScheduleRange,
            "EndTime must be greater than StartTime.",
            "EndTime");

        var (statusCode, body) = await ExecuteExceptionHandlerAsync(ex);

        statusCode.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.InvalidScheduleRange);
        body.GetProperty("errors").GetProperty("EndTime")[0].GetString()
            .Should().Contain("EndTime must be greater than StartTime");
    }

    [Fact]
    public async Task GlobalExceptionHandler_TimeoutRejectedException_Returns504WithRetryAfterSeconds()
    {
        var (statusCode, body) = await ExecuteExceptionHandlerAsync(
            new TimeoutRejectedException("Outer 500ms budget exceeded"));

        statusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.GatewayTimeout);
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task GlobalExceptionHandler_CallerCancellation_ReturnsTrueWithoutWritingProblemDetailsBody()
    {
        var (_, body) = await ExecuteExceptionHandlerAsync(
            new OperationCanceledException("Client disconnected"),
            cancelRequestAborted: true);

        body.ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task GlobalExceptionHandler_UnhandledException_Returns500WithSanitizedMessage()
    {
        var (statusCode, body) = await ExecuteExceptionHandlerAsync(
            new Exception("Sensitive internal SQL stack trace"));

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.InternalError);
        body.GetProperty("detail").GetString().Should().Be("An unexpected internal server error occurred.");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task GlobalExceptionHandler_TransientNpgsqlFailureWrappedByEfCore_Returns503PersistenceUnavailable()
    {
        // Npgsql execution strategy mặc định bọc lỗi kết nối tạm thời trong InvalidOperationException
        var connectionFailure = new NpgsqlException(
            "Failed to connect to 10.0.0.5:5432",
            new SocketException((int)SocketError.ConnectionRefused));
        var wrapped = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.", connectionFailure);

        var (statusCode, body) = await ExecuteExceptionHandlerAsync(wrapped);

        statusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.GetProperty("code").GetString().Should().Be(RuntimeErrorCodes.PersistenceUnavailable);
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(5);
        body.GetProperty("detail").GetString().Should().NotContain("10.0.0.5");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task GlobalExceptionHandler_NonTransientPostgresError_DoesNotMapTo503()
    {
        var uniqueViolation = new DbUpdateException(
            "Unique violation",
            new PostgresException("duplicate key value", "ERROR", "ERROR", "23505"));

        var (statusCode, _) = await ExecuteExceptionHandlerAsync(uniqueViolation);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    #endregion
}
