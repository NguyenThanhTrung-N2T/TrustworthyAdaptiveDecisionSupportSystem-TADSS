using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Services;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using Xunit;

namespace TADSS.UnitTests.Services;

/// <summary>
/// Bộ kiểm thử xác thực các Invariants vòng đời (Lifecycle Invariants) khi phân giải và thực thi đa chiến lược qua DecisionService.
/// </summary>
public class DecisionServiceStrategyResolutionTests
{
    private static TadssDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TadssDbContext(options);
    }

    private static IDecisionCacheService CreateMemoryCacheService()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new TadssCacheOptions());
        var logger = NullLogger<MemoryDecisionCacheService>.Instance;
        return new MemoryDecisionCacheService(memoryCache, options, logger);
    }

    private static IStrategyResolver CreateResolver(string activeStrategyName, params IReservationDecisionStrategy[] customStrategies)
    {
        var strategies = customStrategies.Length > 0
            ? customStrategies
            : [new DefaultDecisionStrategy(), new MockAlternativeDecisionStrategy()];

        var options = Options.Create(new StrategyOptions { ActiveStrategyName = activeStrategyName });
        return new StrategyResolver(strategies, options, NullLogger<StrategyResolver>.Instance);
    }

    private static DecisionService CreateService(
        TadssDbContext dbContext,
        IStrategyResolver resolver,
        IDecisionCacheService? cacheService = null,
        ResilienceOptions? resilienceOptions = null)
    {
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resilienceLogger = NullLogger<DecisionResiliencePipeline>.Instance;
        var resiliencePipeline = new DecisionResiliencePipeline(resilienceOptions ?? new ResilienceOptions(), fallbackStrategy, resilienceLogger);
        var logger = NullLogger<DecisionService>.Instance;
        var effectiveCacheService = cacheService ?? CreateMemoryCacheService();

        return new DecisionService(dbContext, resolver, resiliencePipeline, effectiveCacheService, logger);
    }

    private static DecisionRequestDto CreateSampleRequest(DateTime? startTime = null, decimal totalPrice = 200000m, decimal deposit = 60000m)
    {
        var st = startTime ?? new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc); // Wednesday 18:00 (Peak hour)
        return new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = st,
            EndTime = st.AddHours(2),
            BookingType = "Single",
            TotalPrice = totalPrice,
            DepositAmount = deposit,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task EvaluateDecision_AuditRecord_ContainsResolvedStrategyName()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var resolver = CreateResolver("MockAlternativeDecisionStrategy");
        var service = CreateService(dbContext, resolver);

        var request = CreateSampleRequest();

        // Act
        var result = await service.EvaluateDecisionAsync(request, "audit-name-test");

        // Assert 1: Result metadata matches resolved strategy
        result.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        result.StrategyVersion.Should().Be("1.0.0");

        // Assert 2: Invariant - Audit ledger in Database contains exact resolved strategy identity
        var execution = await dbContext.DecisionExecutions.FirstOrDefaultAsync(e => e.DecisionId == result.DecisionId);
        execution.Should().NotBeNull();
        execution!.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        execution.StrategyVersion.Should().Be("1.0.0");
        execution.ExecutionType.Should().Be(ExecutionType.AUTOMATED.ToString());
    }

    [Fact]
    public async Task EvaluateDecision_StrategyFallback_DoesNotPopulateCache()
    {
        // Arrange: Strategy that throws to activate Fallback
        using var dbContext = CreateInMemoryDbContext();
        var failingStrategy = new FailingStrategyForCacheTest();
        var resolver = CreateResolver("FailingStrategyForCacheTest", failingStrategy);
        var cacheService = CreateMemoryCacheService();
        var service = CreateService(dbContext, resolver, cacheService);

        var request = CreateSampleRequest();
        var hash = RequestHasher.ComputeHash(request);
        var expectedCacheKey = $"decision:hash:{hash}:FailingStrategyForCacheTest:v1.0.0";

        // Act
        var result = await service.EvaluateDecisionAsync(request, "fallback-cache-test");

        // Assert 1: Fallback activated
        result.FallbackUsed.Should().BeTrue();
        result.Status.Should().Be("NEED_APPROVAL");

        // Assert 2: LOCKED INVARIANT - Fallback result MUST NOT pollute decision cache!
        var cached = await cacheService.GetAsync(expectedCacheKey);
        cached.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateDecision_FallbackActivated_AuditRecordsCorrectFallbackIdentity()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var failingStrategy = new FailingStrategyForCacheTest();
        var resolver = CreateResolver("FailingStrategyForCacheTest", failingStrategy);
        var service = CreateService(dbContext, resolver);

        var request = CreateSampleRequest();

        // Act
        var result = await service.EvaluateDecisionAsync(request, "fallback-identity-test");

        // Assert: Invariant - When fallback is triggered, audit ledger records FallbackDecisionStrategy
        var execution = await dbContext.DecisionExecutions.FirstOrDefaultAsync(e => e.DecisionId == result.DecisionId);
        execution.Should().NotBeNull();
        execution!.StrategyName.Should().Be("FallbackDecisionStrategy");
        execution.FallbackUsed.Should().BeTrue();
        execution.ExecutionType.Should().Be(ExecutionType.FALLBACK.ToString());
        execution.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IdempotencyReplay_AfterConfigChange_ReturnsOriginalResult()
    {
        // Arrange: Shared in-memory DB
        using var dbContext = CreateInMemoryDbContext();
        var cacheService = CreateMemoryCacheService();

        // Service 1: Configured with DefaultDecisionStrategy
        var resolverDefault = CreateResolver("DefaultDecisionStrategy");
        var serviceDefault = CreateService(dbContext, resolverDefault, cacheService);

        // Service 2: Configured with MockAlternativeDecisionStrategy
        var resolverMock = CreateResolver("MockAlternativeDecisionStrategy");
        var serviceMock = CreateService(dbContext, resolverMock, cacheService);

        var request = CreateSampleRequest(deposit: 60000m); // 30% deposit on 200k
        var idempotencyKey = "idemp-cross-config-001";

        // Step 1: Execute with Service 1 (Default) -> Deposit 30% on standard booking (under 2M) -> NEED_APPROVAL (Deposit below 50% or below 30%)
        var result1 = await serviceDefault.EvaluateDecisionAsync(request, idempotencyKey);
        result1.StrategyName.Should().Be("DefaultDecisionStrategy");

        // Step 2: System operator changes config to MockAlternativeDecisionStrategy. Client resends same request with same idempotency key.
        var result2 = await serviceMock.EvaluateDecisionAsync(request, idempotencyKey);

        // Assert: LOCKED INVARIANT - Idempotency MUST trump configuration changes!
        // The replayed decision MUST match the original execution (DefaultDecisionStrategy, same DecisionId)
        result2.DecisionId.Should().Be(result1.DecisionId);
        result2.StrategyName.Should().Be("DefaultDecisionStrategy");
        result2.Status.Should().Be(result1.Status);

        // Database still contains only 1 record
        var count = await dbContext.DecisionExecutions.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task IdempotencyConflict_AfterConfigChange_StillDetectsPayloadMismatch()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var resolverDefault = CreateResolver("DefaultDecisionStrategy");
        var serviceDefault = CreateService(dbContext, resolverDefault);

        var resolverMock = CreateResolver("MockAlternativeDecisionStrategy");
        var serviceMock = CreateService(dbContext, resolverMock);

        var requestA = CreateSampleRequest(deposit: 100000m);
        var requestB = CreateSampleRequest(deposit: 200000m); // Different payload
        var idempotencyKey = "idemp-conflict-test";

        // Step 1: Execute requestA with Default
        await serviceDefault.EvaluateDecisionAsync(requestA, idempotencyKey);

        // Step 2: Execute requestB with MockAlternative using SAME idempotency key
        var act = () => serviceMock.EvaluateDecisionAsync(requestB, idempotencyKey);

        // Assert: Invariant - Conflict detected regardless of strategy configuration
        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    [Fact]
    public async Task SamePayload_DifferentStrategies_ProducesDifferentDecisionsAndIsolatedCacheEntries()
    {
        // Arrange: Shared DB and Cache
        using var dbContext = CreateInMemoryDbContext();
        var cacheService = CreateMemoryCacheService();

        var resolverDefault = CreateResolver("DefaultDecisionStrategy");
        var serviceDefault = CreateService(dbContext, resolverDefault, cacheService);

        var resolverMock = CreateResolver("MockAlternativeDecisionStrategy");
        var serviceMock = CreateService(dbContext, resolverMock, cacheService);

        // Wednesday 18:00 (Peak), deposit 50% (100,000 / 200,000 VND)
        var peakWednesday = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        var request = CreateSampleRequest(startTime: peakWednesday, deposit: 100000m);
        var hash = RequestHasher.ComputeHash(request);

        // Act 1: Execute with DefaultDecisionStrategy (deposit 50% -> APPROVED)
        var resultDefault = await serviceDefault.EvaluateDecisionAsync(request, "key-default");
        resultDefault.StrategyName.Should().Be("DefaultDecisionStrategy");
        resultDefault.Status.Should().Be("APPROVED");

        // Act 2: Execute with MockAlternativeDecisionStrategy (peak hour, 50% -> APPROVED with peak reason)
        var resultMock = await serviceMock.EvaluateDecisionAsync(request, "key-mock");
        resultMock.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        resultMock.Status.Should().Be("APPROVED");

        // Assert: Cache keys are completely isolated by StrategyName
        var cacheKeyDefault = $"decision:hash:{hash}:DefaultDecisionStrategy:v1.0.0";
        var cacheKeyMock = $"decision:hash:{hash}:MockAlternativeDecisionStrategy:v1.0.0";

        var cachedDefault = await cacheService.GetAsync(cacheKeyDefault);
        var cachedMock = await cacheService.GetAsync(cacheKeyMock);

        cachedDefault.Should().NotBeNull();
        cachedDefault!.StrategyName.Should().Be("DefaultDecisionStrategy");

        cachedMock.Should().NotBeNull();
        cachedMock!.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
    }

    private sealed class FailingStrategyForCacheTest : IReservationDecisionStrategy
    {
        public string StrategyName => "FailingStrategyForCacheTest";
        public string StrategyVersion => "1.0.0";

        public Task<DecisionResultDto> EvaluateAsync(Shared.DecisionContractSDK.Domain.Reservation reservation, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated external service failure.");
        }
    }
}
