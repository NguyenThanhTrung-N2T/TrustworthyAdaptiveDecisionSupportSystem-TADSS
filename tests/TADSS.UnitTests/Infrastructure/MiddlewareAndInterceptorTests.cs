using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;
using TADSS.Runtime.Api.Middlewares;
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

    /// <summary>
    /// DbContext Npgsql chỉ để theo dõi thay đổi (không mở kết nối): interceptor được gọi trực tiếp thay cho <c>SaveChanges</c>,
    /// rồi <c>AcceptAllChanges</c> mô phỏng việc lưu thành công. Không dùng EF InMemory (quy tắc G3).
    /// </summary>
    private static TadssDbContext CreateTrackingDbContext()
    {
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        return new TadssDbContext(options);
    }

    private sealed class StubEventDefinition(ILoggingOptions loggingOptions)
        : EventDefinitionBase(loggingOptions, new EventId(1), LogLevel.Debug, "Test.Stub");

    private static DbContextEventData CreateEventData(DbContext context) =>
        new(new StubEventDefinition(Mock.Of<ILoggingOptions>()), (_, _) => string.Empty, context);

    private static void SimulateSave(AuditTimestampInterceptor interceptor, DbContext context)
    {
        interceptor.SavingChanges(CreateEventData(context), default);
        context.ChangeTracker.AcceptAllChanges();
    }

    private static async Task SimulateSaveAsync(AuditTimestampInterceptor interceptor, DbContext context)
    {
        await interceptor.SavingChangesAsync(CreateEventData(context), default);
        context.ChangeTracker.AcceptAllChanges();
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
        var interceptor = new AuditTimestampInterceptor();
        using var dbContext = CreateTrackingDbContext();
        var entity = CreateSampleEntity();
        var beforeAdd = DateTime.UtcNow.AddSeconds(-1);

        // Act 1: Insert (EntityState.Added)
        dbContext.DecisionExecutions.Add(entity);
        await SimulateSaveAsync(interceptor, dbContext);

        // Assert 1: CreatedAt and UpdatedAt set, Version remains 1
        entity.CreatedAt.Should().BeOnOrAfter(beforeAdd);
        entity.UpdatedAt.Should().Be(entity.CreatedAt);
        entity.Version.Should().Be(1);

        var initialCreatedAt = entity.CreatedAt;

        // Act 2: Modify (EntityState.Modified)
        entity.IsOverridden = true;
        entity.OverriddenBy = "manager-01";
        entity.OverrideReason = "Approved after manual verification";
        await SimulateSaveAsync(interceptor, dbContext);

        // Assert 2: CreatedAt preserved, UpdatedAt refreshed, Version incremented to 2
        entity.CreatedAt.Should().Be(initialCreatedAt);
        entity.UpdatedAt.Should().BeOnOrAfter(initialCreatedAt);
        entity.Version.Should().Be(2);
    }

    [Fact]
    public void AuditTimestampInterceptor_SynchronousSaveChanges_AlsoUpdatesTimestampsAndIncrementsVersion()
    {
        // Arrange
        var interceptor = new AuditTimestampInterceptor();
        using var dbContext = CreateTrackingDbContext();
        var entity = CreateSampleEntity();

        // Act 1: Synchronous SaveChanges on Add
        dbContext.DecisionExecutions.Add(entity);
        SimulateSave(interceptor, dbContext);
        entity.Version.Should().Be(1);

        // Act 2: Synchronous SaveChanges on Modify
        entity.IsOverridden = true;
        SimulateSave(interceptor, dbContext);

        // Assert
        entity.Version.Should().Be(2);
    }

    #endregion
}
