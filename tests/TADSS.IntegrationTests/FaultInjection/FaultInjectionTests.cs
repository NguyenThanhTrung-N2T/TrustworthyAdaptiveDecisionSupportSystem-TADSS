using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Services;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// Bộ kiểm thử tích hợp tự động cho Ma trận Lỗi (Failure Semantics Matrix F1 ➔ F7)
/// Đặc tả theo docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md.
/// </summary>
public class FaultInjectionTests
{
    private static DecisionRequestDto CreateSampleRequest(Guid? reservationId = null)
    {
        return new DecisionRequestDto
        {
            ReservationId = reservationId ?? Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CourtTypeId = null,
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            TotalPrice = 300000m,
            DepositAmount = 150000m,
            PaymentStatus = "PARTIALLY_PAID",
            BookingType = "SINGLE",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
    }

    #region Mock Strategy and Interceptor Helpers for Fault Injection

    private class FlakyTransientStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "FlakyTransientStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }

        public async Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (InvocationCount == 1)
            {
                // Attempt 1: Timeout quá ngưỡng inner attempt (delay 250ms trên inner timeout 100ms)
                await Task.Delay(250, cancellationToken);
            }

            return new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = DecisionStatus.APPROVED.ToString(),
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = 15,
                FallbackUsed = false,
                PrimaryReason = "RETRY_SUCCEEDED",
                Reasons = ["Thành công ở lần retry thứ 2"],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            };
        }
    }

    private class ExhaustiveTimeoutStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "ExhaustiveTimeoutStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }

        public async Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            await Task.Delay(500, cancellationToken);

            return new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = DecisionStatus.APPROVED.ToString(),
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = 500,
                FallbackUsed = false,
                PrimaryReason = "UNREACHABLE",
                Reasons = [],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            };
        }
    }

    private class CrashStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "CrashStrategy";
        public string StrategyVersion => "1.0.0";

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Fatal third-party AI provider crashed with code 500.");
        }
    }

    private class SingleStrategyResolver : IStrategyResolver
    {
        private readonly IReservationDecisionStrategy _strategy;

        public SingleStrategyResolver(IReservationDecisionStrategy strategy)
        {
            _strategy = strategy;
        }

        public IReservationDecisionStrategy ResolveActive() => _strategy;
        public IReservationDecisionStrategy ResolveByName(string strategyName) => _strategy;
        public IReadOnlyList<StrategyRegistration> ListRegistered() =>
            [new StrategyRegistration(_strategy.StrategyName, _strategy.StrategyVersion)];
    }

    private class FailingCacheService : IDecisionCacheService
    {
        public Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis connection refused / cluster down.");
        }

        public Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis write timeout / cluster down.");
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Redis remove failed.");
        }
    }

    private class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Database connection pool exhausted / disk write failure.");
        }
    }

    #endregion

    [Fact]
    public async Task F1_PerAttemptTimeoutInBudget_RetriesAndReturnsOriginalStrategyResult()
    {
        // Arrange
        var flakyStrategy = new FlakyTransientStrategy();
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStrategyResolver>(new SingleStrategyResolver(flakyStrategy));
                services.Configure<ResilienceOptions>(opts =>
                {
                    opts.InnerTimeoutMs = 100;
                    opts.OuterTimeoutMs = 1000;
                    opts.MaxRetryAttempts = 2;
                    opts.RetryBaseDelayMs = 20;
                });
            });
        });

        var client = factory.CreateClient();
        var request = CreateSampleRequest();
        var idempotencyKey = "F1-KEY-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/decisions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("APPROVED");
        result.PrimaryReason.Should().Be("RETRY_SUCCEEDED");
        result.FallbackUsed.Should().BeFalse();

        flakyStrategy.InvocationCount.Should().Be(2); // Attempt 1 timed out -> Attempt 2 succeeded

        // Verify Database Record
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var audit = await db.DecisionExecutions.FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
        audit.Should().NotBeNull();
        audit!.ExecutionType.Should().Be("AUTOMATED");
        audit.FallbackUsed.Should().BeFalse();
        audit.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task F2_TimeoutExhaustsRetries_ActivatesDeterministicFallbackWithTimeoutReason()
    {
        // Arrange
        var timeoutStrategy = new ExhaustiveTimeoutStrategy();
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStrategyResolver>(new SingleStrategyResolver(timeoutStrategy));
                services.Configure<ResilienceOptions>(opts =>
                {
                    opts.InnerTimeoutMs = 50;
                    opts.OuterTimeoutMs = 2000;
                    opts.MaxRetryAttempts = 1;
                    opts.RetryBaseDelayMs = 10;
                });
            });
        });

        var client = factory.CreateClient();
        var request = CreateSampleRequest();
        var idempotencyKey = "F2-KEY-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/decisions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("NEED_APPROVAL");
        result.FallbackUsed.Should().BeTrue();

        timeoutStrategy.InvocationCount.Should().Be(2); // Initial attempt + 1 retry

        // Verify Database Record
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var audit = await db.DecisionExecutions.FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
        audit.Should().NotBeNull();
        audit!.ExecutionType.Should().Be("FALLBACK");
        audit.FallbackUsed.Should().BeTrue();
        audit.FailureReason.Should().Contain(FailureReasonPrefixes.StrategyTimeout);
    }

    [Fact]
    public async Task F3_StrategyInternalException_ActivatesDeterministicFallbackWithExceptionReason()
    {
        // Arrange
        var crashStrategy = new CrashStrategy();
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStrategyResolver>(new SingleStrategyResolver(crashStrategy));
                services.Configure<ResilienceOptions>(opts =>
                {
                    opts.MaxRetryAttempts = 1;
                    opts.RetryBaseDelayMs = 10;
                });
            });
        });

        var client = factory.CreateClient();
        var request = CreateSampleRequest();
        var idempotencyKey = "F3-KEY-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/decisions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("NEED_APPROVAL");
        result.FallbackUsed.Should().BeTrue();

        // Verify Database Record
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var audit = await db.DecisionExecutions.FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
        audit.Should().NotBeNull();
        audit!.ExecutionType.Should().Be("FALLBACK");
        audit.FallbackUsed.Should().BeTrue();
        audit.FailureReason.Should().Contain(FailureReasonPrefixes.StrategyException);
    }

    [Fact]
    public async Task F4_CircuitBreakerOpen_FastFailsDirectlyToFallback()
    {
        // Arrange
        var crashStrategy = new CrashStrategy();
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStrategyResolver>(new SingleStrategyResolver(crashStrategy));
                services.Configure<ResilienceOptions>(opts =>
                {
                    opts.CircuitBreakerMinimumThroughput = 2;
                    opts.CircuitBreakerFailureRatio = 0.5;
                    opts.CircuitBreakerSamplingSeconds = 30;
                    opts.CircuitBreakerBreakDurationSeconds = 10;
                    opts.MaxRetryAttempts = 0;
                });
            });
        });

        var client = factory.CreateClient();

        // Trigger circuit to OPEN by sending 2 failed requests
        for (int i = 0; i < 2; i++)
        {
            var req = CreateSampleRequest();
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
            {
                Content = JsonContent.Create(req)
            };
            msg.Headers.Add("X-Idempotency-Key", $"F4-PRE-{i}-{Guid.NewGuid()}");
            await client.SendAsync(msg);
        }

        // Act: Third request when Circuit Breaker is OPEN
        var testRequest = CreateSampleRequest();
        var idempotencyKey = "F4-KEY-" + Guid.NewGuid();
        using var testMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(testRequest)
        };
        testMsg.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(testMsg);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>();
        result.Should().NotBeNull();
        result!.FallbackUsed.Should().BeTrue();

        // Verify Database Record
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var audit = await db.DecisionExecutions.FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
        audit.Should().NotBeNull();
        audit!.ExecutionType.Should().Be("FALLBACK");
        audit.FallbackUsed.Should().BeTrue();
        audit.FailureReason.Should().Contain(FailureReasonPrefixes.CircuitBreakerOpen);
    }

    [Fact]
    public async Task F5_RedisUnavailable_DegradesGracefullyWithCacheMissSemantics()
    {
        // Arrange: Inject failing cache service
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace IDecisionCacheService with a failing one
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDecisionCacheService));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IDecisionCacheService>(new FailingCacheService());
            });
        });

        var client = factory.CreateClient();
        var request = CreateSampleRequest();
        var idempotencyKey = "F5-KEY-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);

        // Act: Request arrives while cache is down
        var response = await client.PostAsJsonAsync("/api/v1/decisions", request);

        // Assert: System must NOT crash, must NOT fallback strategy, must execute strategy normally
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>();
        result.Should().NotBeNull();
        result!.FallbackUsed.Should().BeFalse(); // Normal strategy executed!

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var audit = await db.DecisionExecutions.FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
        audit.Should().NotBeNull();
        audit!.CacheHit.Should().BeFalse();
        audit.FallbackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task F6_DatabasePersistenceFailure_FailsClosedWithHttp500WithoutReturningDecision()
    {
        // Arrange: Inject failing DbContext interceptor (simulating DB down/write failure)
        var dbName = Guid.NewGuid().ToString();
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TadssDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<TadssDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(dbName);
                    options.AddInterceptors(new FailingSaveChangesInterceptor());
                });
            });
        });

        var client = factory.CreateClient();
        var request = CreateSampleRequest();
        var idempotencyKey = "F6-KEY-" + Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);

        // Act: Client submits decision request
        var response = await client.PostAsJsonAsync("/api/v1/decisions", request);

        // Assert: Policy A (Fail-Closed) => Must return HTTP 500 ProblemDetails and NOT return 200 OK Decision
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(500);
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]?.ToString().Should().Be(ErrorCodes.InternalError);
    }

    [Fact]
    public async Task F7_CallerCancellation_AbortsPromptlyWithoutCreatingAuditRecord()
    {
        // Arrange: Strategy that takes 500ms
        var slowStrategy = new ExhaustiveTimeoutStrategy();
        var factory = new TadssWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStrategyResolver>(new SingleStrategyResolver(slowStrategy));
            });
        });

        var client = factory.CreateClient();
        var request = CreateSampleRequest();
        var idempotencyKey = "F7-KEY-" + Guid.NewGuid();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50); // Cancel client request after 50ms

        // Act: Client aborts
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var act = () => client.SendAsync(httpRequest, cts.Token);

        // Assert: Client catches cancellation
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Verify Invariant: NO completed decision record must exist in DB
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var audit = await db.DecisionExecutions.FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);
        audit.Should().BeNull(); // Invariant: Zero audit pollution on cancellation!
    }
}
