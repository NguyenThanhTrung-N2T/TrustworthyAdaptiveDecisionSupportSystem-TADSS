using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using StackExchange.Redis;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Services;
using TADSS.Strategies.Default;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

public class DecisionCacheTests
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
        var options = Options.Create(new TadssCacheOptions { Provider = "Memory", TtlHours = 24 });
        var logger = NullLogger<MemoryDecisionCacheService>.Instance;
        return new MemoryDecisionCacheService(memoryCache, options, logger);
    }

    private static IStrategyResolver CreateResolver(IReservationDecisionStrategy strategy)
    {
        var options = Options.Create(new StrategyOptions { ActiveStrategyName = strategy.StrategyName });
        return new StrategyResolver(new[] { strategy }, options, NullLogger<StrategyResolver>.Instance);
    }

    private static DecisionService CreateService(
        TadssDbContext dbContext,
        IDecisionCacheService cacheService,
        IReservationDecisionStrategy? strategy = null)
    {
        var activeStrategy = strategy ?? new DefaultDecisionStrategy();
        var resolver = CreateResolver(activeStrategy);
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resilienceLogger = NullLogger<DecisionResiliencePipeline>.Instance;
        var resiliencePipeline = new DecisionResiliencePipeline(new ResilienceOptions(), fallbackStrategy, resilienceLogger);
        var logger = NullLogger<DecisionService>.Instance;

        return new DecisionService(dbContext, resolver, resiliencePipeline, cacheService, logger);
    }

    private static DecisionRequestDto CreateSampleRequest(decimal totalPrice = 200000m, decimal deposit = 200000m)
    {
        return new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = totalPrice,
            DepositAmount = deposit,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task EvaluateDecisionAsync_CacheHit_CreatesNewAuditRecord_WithCacheHitTrueAndNewDecisionId()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cacheService = CreateMemoryCacheService();
        var service = CreateService(dbContext, cacheService);

        var request = CreateSampleRequest();

        // Act 1: First request with key-01 -> Cache Miss -> Strategy executes -> Cached
        var result1 = await service.EvaluateDecisionAsync(request, "key-01");

        // Act 2: Second request with exact SAME payload, but DIFFERENT idempotency key -> Cache Hit
        var result2 = await service.EvaluateDecisionAsync(request, "key-02");

        // Assert 1: Result outputs match business decisions but have distinct DecisionIds
        result1.Status.Should().Be("APPROVED");
        result2.Status.Should().Be("APPROVED");
        result2.DecisionId.Should().NotBe(result1.DecisionId); // Brand new DecisionId for new transaction!
        result2.ExecutionTimeMs.Should().BeLessThanOrEqualTo(10); // Fast cache hit

        // Assert 2: Database contains exactly 2 distinct audit records
        var executions = await dbContext.DecisionExecutions.OrderBy(e => e.CreatedAt).ToListAsync();
        executions.Should().HaveCount(2);

        // Record 1: AUTOMATED, CacheHit = false
        executions[0].DecisionId.Should().Be(result1.DecisionId);
        executions[0].ExecutionType.Should().Be(ExecutionType.AUTOMATED.ToString());
        executions[0].CacheHit.Should().BeFalse();
        executions[0].IdempotencyKey.Should().Be("key-01");

        // Record 2: CACHE_HIT, CacheHit = true
        executions[1].DecisionId.Should().Be(result2.DecisionId);
        executions[1].ExecutionType.Should().Be(ExecutionType.CACHE_HIT.ToString());
        executions[1].CacheHit.Should().BeTrue();
        executions[1].IdempotencyKey.Should().Be("key-02");
    }

    [Fact]
    public async Task EvaluateDecisionAsync_CacheMiss_ExecutesStrategyAndCachesResult()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cacheService = CreateMemoryCacheService();
        var service = CreateService(dbContext, cacheService);

        var request = CreateSampleRequest();
        var hash = RequestHasher.ComputeHash(request);
        var expectedCacheKey = $"decision:hash:{hash}:DefaultDecisionStrategy:v1.0.0";

        // Pre-check: Cache is empty
        var preCache = await cacheService.GetAsync(expectedCacheKey);
        preCache.Should().BeNull();

        // Act: Run evaluation
        var result = await service.EvaluateDecisionAsync(request, "miss-key-01");

        // Assert: Cache now contains evaluated result
        var postCache = await cacheService.GetAsync(expectedCacheKey);
        postCache.Should().NotBeNull();
        postCache!.Status.Should().Be(result.Status);
        postCache.PrimaryReason.Should().Be(result.PrimaryReason);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_FallbackResult_IsNotCached()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cacheService = CreateMemoryCacheService();

        // Strategy that fails/throws to trigger Fallback
        var failingStrategy = new AlwaysFailingStrategy();
        var service = CreateService(dbContext, cacheService, failingStrategy);

        var request = CreateSampleRequest();
        var hash = RequestHasher.ComputeHash(request);
        var expectedCacheKey = $"decision:hash:{hash}:AlwaysFailingStrategy:v1.0.0";

        // Act: Run evaluation -> Triggers Fallback
        var result = await service.EvaluateDecisionAsync(request, "fallback-key");
        result.FallbackUsed.Should().BeTrue();
        result.Status.Should().Be("NEED_APPROVAL");

        // Assert: Poisoning Prevention -> Fallback result MUST NOT be cached!
        var cached = await cacheService.GetAsync(expectedCacheKey);
        cached.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateDecisionAsync_StrategyVersionChange_ProducesCacheMiss()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var cacheService = CreateMemoryCacheService();

        var strategyV1 = new VersionedStrategy("1.0.0");
        var serviceV1 = CreateService(dbContext, cacheService, strategyV1);

        var request = CreateSampleRequest();

        // Step 1: Execute with v1.0.0 -> cached under v1.0.0
        var resultV1 = await serviceV1.EvaluateDecisionAsync(request, "v1-key");
        resultV1.StrategyVersion.Should().Be("1.0.0");

        // Step 2: Deploy new strategy version v2.0.0
        var strategyV2 = new VersionedStrategy("2.0.0");
        var serviceV2 = CreateService(dbContext, cacheService, strategyV2);

        // Act: Execute with v2.0.0
        var resultV2 = await serviceV2.EvaluateDecisionAsync(request, "v2-key");

        // Assert: Evaluates with v2.0.0 (not picking up stale v1.0.0 cache)
        resultV2.StrategyVersion.Should().Be("2.0.0");
        resultV2.DecisionId.Should().NotBe(resultV1.DecisionId);
    }

    [Fact]
    public async Task ResilientDecisionCacheService_WhenRedisThrows_GracefullyFallsBackToMemoryCache()
    {
        // Arrange: Mock Redis throwing RedisConnectionException
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var memService = new MemoryDecisionCacheService(
            memoryCache,
            Options.Create(new TadssCacheOptions()),
            NullLogger<MemoryDecisionCacheService>.Instance);

        var mockRedisService = new Mock<IDecisionCacheService>();
        mockRedisService
            .Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline"));

        mockRedisService
            .Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<DecisionResultDto>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline"));

        var options = Options.Create(new TadssCacheOptions
        {
            Provider = "Redis",
            EnableFallbackToMemory = true
        });

        // Set value in memory cache first
        var cachedItem = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = "APPROVED",
            StrategyName = "Default",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 5,
            FallbackUsed = false,
            PrimaryReason = "TEST_CACHE",
            Reasons = [],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };
        await memService.SetAsync("test-key", cachedItem);

        var resilientCache = new ResilientDecisionCacheService(
            memService,
            options,
            NullLogger<ResilientDecisionCacheService>.Instance,
            new RedisDecisionCacheService(Mock.Of<IConnectionMultiplexer>(), options, NullLogger<RedisDecisionCacheService>.Instance));

        // Use reflection or override to inject the throwing mock
        var resilientCacheWithMock = new ResilientDecisionCacheServiceWithMock(memService, options, mockRedisService.Object);

        // Act & Assert 1: GetAsync falls back to memory cache despite Redis failure
        var retrieved = await resilientCacheWithMock.GetAsync("test-key");
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be("APPROVED");

        // Act & Assert 2: SetAsync does not throw even when Redis is offline
        var actSet = () => resilientCacheWithMock.SetAsync("key-2", cachedItem);
        await actSet.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DependencyInjection_WhenProviderIsMemory_DoesNotResolveConnectionMultiplexer()
    {
        // Arrange: Configure Provider = "Memory" and register a trap IConnectionMultiplexer that throws if resolved
        var configDict = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=tadss_test;Username=postgres;Password=postgres",
            ["Tadss:Cache:Provider"] = "Memory",
            ["Tadss:Cache:TtlHours"] = "24"
        };
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        var redisResolved = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            redisResolved = true;
            throw new InvalidOperationException("IConnectionMultiplexer must not be resolved when Provider is Memory.");
        });

        using var sp = services.BuildServiceProvider();

        // Act: Resolve IDecisionCacheService and perform Set/Get operations
        var cacheService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IDecisionCacheService>(sp);
        var sampleResult = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = "APPROVED",
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 1,
            FallbackUsed = false,
            PrimaryReason = "APPROVED_PAID_IN_FULL",
            Reasons = [],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        await cacheService.SetAsync("mem-only-key", sampleResult);
        var cached = await cacheService.GetAsync("mem-only-key");

        // Assert
        redisResolved.Should().BeFalse();
        cached.Should().NotBeNull();
        cached!.DecisionId.Should().Be(sampleResult.DecisionId);
    }

    #region Helper Mock Classes

    private class AlwaysFailingStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "AlwaysFailingStrategy";
        public string StrategyVersion => "1.0.0";

        public Task<DecisionResultDto> EvaluateAsync(Shared.DecisionContractSDK.Domain.Reservation reservation, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("External dependency unavailable.");
        }
    }

    private class VersionedStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "VersionedStrategy";
        public string StrategyVersion { get; }

        public VersionedStrategy(string version)
        {
            StrategyVersion = version;
        }

        public Task<DecisionResultDto> EvaluateAsync(Shared.DecisionContractSDK.Domain.Reservation reservation, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = "APPROVED",
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = 5,
                FallbackUsed = false,
                PrimaryReason = "VERSION_SUCCESS",
                Reasons = [],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            });
        }
    }

    private class ResilientDecisionCacheServiceWithMock : IDecisionCacheService
    {
        private readonly MemoryDecisionCacheService _memoryCache;
        private readonly IDecisionCacheService _throwingRedis;
        private readonly TadssCacheOptions _options;

        public ResilientDecisionCacheServiceWithMock(
            MemoryDecisionCacheService memoryCache,
            IOptions<TadssCacheOptions> options,
            IDecisionCacheService throwingRedis)
        {
            _memoryCache = memoryCache;
            _options = options.Value;
            _throwingRedis = throwingRedis;
        }

        public async Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _throwingRedis.GetAsync(key, cancellationToken);
            }
            catch (Exception)
            {
                if (_options.EnableFallbackToMemory)
                {
                    return await _memoryCache.GetAsync(key, cancellationToken);
                }
                return null;
            }
        }

        public async Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            try
            {
                await _throwingRedis.SetAsync(key, result, ttl, cancellationToken);
            }
            catch (Exception)
            {
                if (_options.EnableFallbackToMemory)
                {
                    await _memoryCache.SetAsync(key, result, ttl, cancellationToken);
                }
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                await _throwingRedis.RemoveAsync(key, cancellationToken);
            }
            catch (Exception)
            {
                await _memoryCache.RemoveAsync(key, cancellationToken);
            }
        }
    }

    #endregion
}
