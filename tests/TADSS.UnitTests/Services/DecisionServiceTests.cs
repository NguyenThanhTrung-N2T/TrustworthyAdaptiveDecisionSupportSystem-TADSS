using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Services;
using TADSS.Strategies.Default;
using Xunit;

namespace TADSS.UnitTests.Services;

public class DecisionServiceTests
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
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new TADSS.Runtime.Api.Infrastructure.Caching.TadssCacheOptions());
        var logger = NullLogger<TADSS.Runtime.Api.Infrastructure.Caching.MemoryDecisionCacheService>.Instance;
        return new TADSS.Runtime.Api.Infrastructure.Caching.MemoryDecisionCacheService(memoryCache, options, logger);
    }

    private static IStrategyResolver CreateResolver(IReservationDecisionStrategy? strategy = null)
    {
        var activeStrategy = strategy ?? new DefaultDecisionStrategy();
        var options = Microsoft.Extensions.Options.Options.Create(new StrategyOptions { ActiveStrategyName = activeStrategy.StrategyName });
        return new StrategyResolver(new[] { activeStrategy }, options, NullLogger<StrategyResolver>.Instance);
    }

    private static DecisionService CreateService(TadssDbContext dbContext, IDecisionCacheService? cacheService = null, IReservationDecisionStrategy? strategy = null)
    {
        var resolver = CreateResolver(strategy);
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resilienceLogger = NullLogger<DecisionResiliencePipeline>.Instance;
        var resiliencePipeline = new DecisionResiliencePipeline(new ResilienceOptions(), fallbackStrategy, resilienceLogger);
        var logger = NullLogger<DecisionService>.Instance;
        var effectiveCacheService = cacheService ?? CreateMemoryCacheService();

        return new DecisionService(dbContext, resolver, resiliencePipeline, effectiveCacheService, logger);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_FullyPaidBooking_ReturnsApprovedAndPersistsRecord()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 200000m, // 100% paid
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var result = await service.EvaluateDecisionAsync(request, "idemp-001");

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("APPROVED");
        result.PrimaryReason.Should().Be("APPROVED_PAID_IN_FULL");
        result.FallbackUsed.Should().BeFalse();

        // Verify Database Persistence
        var persisted = await dbContext.DecisionExecutions.FirstOrDefaultAsync(e => e.DecisionId == result.DecisionId);
        persisted.Should().NotBeNull();
        persisted!.ReservationId.Should().Be(request.ReservationId);
        persisted.NeedApproval.Should().BeFalse();
        persisted.IsOverridden.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateDecisionAsync_DepositBelowThreshold_ReturnsNeedApprovalWithRecommendation()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 30000m, // 10% paid (< 30%)
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var result = await service.EvaluateDecisionAsync(request, "idemp-002");

        // Assert
        result.Status.Should().Be("NEED_APPROVAL");
        result.PrimaryReason.Should().Be("DEPOSIT_BELOW_THRESHOLD");
        result.Recommendations.Should().NotBeEmpty();
        result.Recommendations.First().Type.Should().Be("REQUIRE_DEPOSIT");

        // Verify DB
        var persisted = await dbContext.DecisionExecutions.FirstOrDefaultAsync(e => e.DecisionId == result.DecisionId);
        persisted.Should().NotBeNull();
        persisted!.NeedApproval.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateDecisionAsync_IdempotencyMatch_ReplaysCachedResult()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 500000m,
            DepositAmount = 0m,
            PaymentStatus = "UNPAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        var idempotencyKey = "idemp-replay-test";

        // Act 1: Initial call
        var firstResult = await service.EvaluateDecisionAsync(request, idempotencyKey);

        // Act 2: Replay with same key & same payload
        var secondResult = await service.EvaluateDecisionAsync(request, idempotencyKey);

        // Assert
        secondResult.DecisionId.Should().Be(firstResult.DecisionId);
        secondResult.Status.Should().Be(firstResult.Status);

        // Verify only 1 record exists in DB
        var count = await dbContext.DecisionExecutions.CountAsync(e => e.IdempotencyKey == idempotencyKey);
        count.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_IdempotencyMismatch_ThrowsIdempotencyConflictException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var request1 = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 500000m,
            DepositAmount = 0m,
            PaymentStatus = "UNPAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        var request2 = new DecisionRequestDto
        {
            ReservationId = request1.ReservationId,
            CourtId = request1.CourtId,
            BranchId = request1.BranchId,
            CustomerId = request1.CustomerId,
            CustomerTier = request1.CustomerTier,
            StartTime = request1.StartTime,
            EndTime = request1.EndTime,
            BookingType = request1.BookingType,
            TotalPrice = 900000m, // Changed payload!
            DepositAmount = request1.DepositAmount,
            PaymentStatus = request1.PaymentStatus,
            Priority = request1.Priority,
            RequestedAt = request1.RequestedAt
        };

        var idempotencyKey = "idemp-conflict-test";

        // Act 1: First call succeeds
        await service.EvaluateDecisionAsync(request1, idempotencyKey);

        // Act 2: Second call with same key but different payload -> must throw
        var act = () => service.EvaluateDecisionAsync(request2, idempotencyKey);

        // Assert
        await act.Should().ThrowAsync<IdempotencyConflictException>()
            .Where(e => e.IdempotencyKey == idempotencyKey);

        // Verify still only 1 record exists in DB
        var count = await dbContext.DecisionExecutions.CountAsync(e => e.IdempotencyKey == idempotencyKey);
        count.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_ReplayAfterOverride_ReturnsOriginalMachineSnapshot_WhileDetailShowsOverridden()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 30000m, // NEED_APPROVAL
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var idempotencyKey = "idemp-override-replay-test";

        // Act 1: Initial decision -> NEED_APPROVAL
        var initialResult = await service.EvaluateDecisionAsync(request, idempotencyKey);
        initialResult.Status.Should().Be("NEED_APPROVAL");

        // Act 2: Manager overrides to APPROVED
        var overrideRequest = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager-bob",
            OverrideReason = "Khách quen VIP"
        };
        await service.OverrideDecisionAsync(initialResult.DecisionId, overrideRequest);

        // Act 3: Client replays request with same idempotency key
        var replayResult = await service.EvaluateDecisionAsync(request, idempotencyKey);

        // Assert 1: Replay preserves original machine decision snapshot (Invariant 3)
        replayResult.DecisionId.Should().Be(initialResult.DecisionId);
        replayResult.Status.Should().Be("NEED_APPROVAL"); // Original machine status preserved!

        // Assert 2: GET /decisions/{id} returns current governance state
        var detail = await service.GetDecisionDetailAsync(initialResult.DecisionId);
        detail.Should().NotBeNull();
        detail!.Governance.IsOverridden.Should().BeTrue();
        detail.Governance.OverriddenBy.Should().Be("manager-bob");
        detail.Governance.OverrideReason.Should().Be("Khách quen VIP");
    }

    [Fact]
    public async Task GetDecisionDetailAsync_ExistingDecision_ReturnsDetailedDto()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 50000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var evalResult = await service.EvaluateDecisionAsync(request, "idemp-detail-001");

        // Act
        var detail = await service.GetDecisionDetailAsync(evalResult.DecisionId);

        // Assert
        detail.Should().NotBeNull();
        detail!.DecisionId.Should().Be(evalResult.DecisionId);
        detail.ReservationId.Should().Be(request.ReservationId);
        detail.Decision.Status.Should().Be("NEED_APPROVAL");
        detail.Execution.StrategyName.Should().Be("DefaultDecisionStrategy");
        detail.Governance.NeedApproval.Should().BeTrue();
        detail.Governance.IsOverridden.Should().BeFalse();
    }

    [Fact]
    public async Task GetPendingApprovalsAsync_ReturnsOnlyPendingAndNonOverriddenItems()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        // Item 1: NeedApproval
        var req1 = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 20000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
        var res1 = await service.EvaluateDecisionAsync(req1, "pending-1");

        // Item 2: Approved (Not pending)
        var req2 = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 300000m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
        await service.EvaluateDecisionAsync(req2, "pending-2");

        // Act
        var pendingResponse = await service.GetPendingApprovalsAsync(1, 10);

        // Assert
        pendingResponse.TotalItems.Should().Be(1);
        pendingResponse.Items.Should().HaveCount(1);
        pendingResponse.Items.First().DecisionId.Should().Be(res1.DecisionId);
    }

    [Fact]
    public async Task OverrideDecisionAsync_EligibleDecision_UpdatesGovernanceFlagsCorrectly()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var req = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 30000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
        var evalResult = await service.EvaluateDecisionAsync(req, "override-001");

        var overrideRequest = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager-alice",
            OverrideReason = "Khách quen bảo lãnh cọc qua điện thoại"
        };

        // Act
        var overrideResponse = await service.OverrideDecisionAsync(evalResult.DecisionId, overrideRequest);

        // Assert
        overrideResponse.Should().NotBeNull();
        overrideResponse!.OriginalStatus.Should().Be("NEED_APPROVAL");
        overrideResponse.OverriddenStatus.Should().Be("APPROVED");
        overrideResponse.IsOverridden.Should().BeTrue();
        overrideResponse.OverriddenBy.Should().Be("manager-alice");

        // Invariant 3 check: Verify original ResponsePayload is still intact
        var persisted = await dbContext.DecisionExecutions.FirstOrDefaultAsync(e => e.DecisionId == evalResult.DecisionId);
        persisted!.IsOverridden.Should().BeTrue();
        persisted.OverriddenBy.Should().Be("manager-alice");
        persisted.OverrideReason.Should().Be("Khách quen bảo lãnh cọc qua điện thoại");
        persisted.ResponsePayload.Should().Contain("NEED_APPROVAL"); // Snapshot preserved!
    }

    [Fact]
    public async Task EvaluateDecisionAsync_CallerCancellation_ThrowsOperationCanceledException_AndDoesNotPersistAudit()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var slowStrategy = new SlowStrategyForService(delayMs: 500);
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resilienceLogger = NullLogger<DecisionResiliencePipeline>.Instance;
        var resiliencePipeline = new DecisionResiliencePipeline(new ResilienceOptions(), fallbackStrategy, resilienceLogger);
        var logger = NullLogger<DecisionService>.Instance;
        var cacheService = CreateMemoryCacheService();
        var service = new DecisionService(dbContext, CreateResolver(slowStrategy), resiliencePipeline, cacheService, logger);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 200000m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        // Act
        var act = () => service.EvaluateDecisionAsync(request, "idemp-cancel-test", cts.Token);

        // Assert 1: Throws OperationCanceledException
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert 2: ZERO records persisted in Database (No fake audit created)
        var count = await dbContext.DecisionExecutions.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_OuterTimeout_ThrowsTimeoutRejectedException_AndDoesNotPersistAudit()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var slowStrategy = new SlowStrategyForService(delayMs: 300);
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resilienceLogger = NullLogger<DecisionResiliencePipeline>.Instance;
        var options = new ResilienceOptions
        {
            OuterTimeoutMs = 50,
            InnerTimeoutMs = 200,
            MaxRetryAttempts = 0
        };
        var resiliencePipeline = new DecisionResiliencePipeline(options, fallbackStrategy, resilienceLogger);
        var logger = NullLogger<DecisionService>.Instance;
        var cacheService = CreateMemoryCacheService();
        var service = new DecisionService(dbContext, CreateResolver(slowStrategy), resiliencePipeline, cacheService, logger);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 200000m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var act = () => service.EvaluateDecisionAsync(request, "idemp-outer-timeout");

        // Assert 1: Throws TimeoutRejectedException (Runtime execution boundary contract violated)
        await act.Should().ThrowAsync<Polly.Timeout.TimeoutRejectedException>();

        // Assert 2: ZERO records persisted in Database
        var count = await dbContext.DecisionExecutions.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_InnerTimeoutAndRetries_TriggersFallbackAndPersistsAudit()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var slowStrategy = new SlowStrategyForService(delayMs: 100);
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resilienceLogger = NullLogger<DecisionResiliencePipeline>.Instance;
        var options = new ResilienceOptions
        {
            InnerTimeoutMs = 30, // Inner timeout fires on each attempt
            OuterTimeoutMs = 2000,
            MaxRetryAttempts = 1,
            RetryBaseDelayMs = 10
        };
        var resiliencePipeline = new DecisionResiliencePipeline(options, fallbackStrategy, resilienceLogger);
        var logger = NullLogger<DecisionService>.Instance;
        var cacheService = CreateMemoryCacheService();
        var service = new DecisionService(dbContext, CreateResolver(slowStrategy), resiliencePipeline, cacheService, logger);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 200000m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var result = await service.EvaluateDecisionAsync(request, "idemp-inner-fallback");

        // Assert 1: Returns safe Fallback outcome
        result.Should().NotBeNull();
        result.Status.Should().Be("NEED_APPROVAL");
        result.FallbackUsed.Should().BeTrue();
        result.PrimaryReason.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.FallbackServiceTimeout);

        // Assert 2: Audit record persisted in Database with FALLBACK
        var persisted = await dbContext.DecisionExecutions.FirstOrDefaultAsync(e => e.DecisionId == result.DecisionId);
        persisted.Should().NotBeNull();
        persisted!.ExecutionType.Should().Be("FALLBACK");
        persisted.FallbackUsed.Should().BeTrue();
        persisted.FailureReason.Should().Contain("timed out");
    }

    [Fact]
    public async Task EvaluateAndOverride_WithMediatRInProcessPublisher_PersistsAppendOnlyAuditTrailSynchronously()
    {
        // Arrange: Wire MediatR IPublisher & INotificationHandlers with TadssDbContext
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<TadssDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DecisionService).Assembly));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<MediatR.IPublisher>();

        var resolver = CreateResolver();
        var fallbackStrategy = new FallbackDecisionStrategy();
        var resiliencePipeline = new DecisionResiliencePipeline(new ResilienceOptions(), fallbackStrategy, NullLogger<DecisionResiliencePipeline>.Instance);
        var cacheService = CreateMemoryCacheService();
        var service = new DecisionService(dbContext, resolver, resiliencePipeline, cacheService, NullLogger<DecisionService>.Instance, publisher);

        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 400000m,
            DepositAmount = 40000m, // 10% -> NEED_APPROVAL (defaults to 100% requirement)
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Act 1: Evaluate decision via MediatR in-process audit notification
        var evalResult = await service.EvaluateDecisionAsync(request, "idemp-mediatr-audit-01");

        // Assert 1: Persisted synchronously with AUTOMATED execution type
        var persistedBeforeOverride = await dbContext.DecisionExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DecisionId == evalResult.DecisionId);
        persistedBeforeOverride.Should().NotBeNull();
        persistedBeforeOverride!.ExecutionType.Should().Be("AUTOMATED");
        persistedBeforeOverride.NeedApproval.Should().BeTrue();
        persistedBeforeOverride.IsOverridden.Should().BeFalse();

        // Act 2: Override decision via MediatR in-process override notification
        var overrideReq = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager-mediatr",
            OverrideReason = "Approved via synchronous MediatR in-process handler"
        };
        var overrideResult = await service.OverrideDecisionAsync(evalResult.DecisionId, overrideReq);

        // Assert 2: Governance metadata updated while machine ResponsePayload snapshot remains immutable
        overrideResult.Should().NotBeNull();
        overrideResult!.IsOverridden.Should().BeTrue();
        var persistedAfterOverride = await dbContext.DecisionExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DecisionId == evalResult.DecisionId);
        persistedAfterOverride!.IsOverridden.Should().BeTrue();
        persistedAfterOverride.OverriddenBy.Should().Be("manager-mediatr");
        persistedAfterOverride.ResponsePayload.Should().Be(persistedBeforeOverride.ResponsePayload);
    }

    [Theory]
    [InlineData("NEED_APPROVAL")]
    [InlineData("PENDING")]
    [InlineData("INVALID_STATUS")]
    [InlineData("")]
    public async Task OverrideDecisionAsync_InvalidTargetStatus_ThrowsArgumentException(string invalidStatus)
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = CreateService(dbContext);

        var req = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 30000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };
        var evalResult = await service.EvaluateDecisionAsync(req, $"override-invalid-{Guid.NewGuid()}");

        var overrideRequest = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = invalidStatus },
            OverriddenBy = "manager-alice",
            OverrideReason = "Testing invalid target status"
        };

        // Act
        var act = () => service.OverrideDecisionAsync(evalResult.DecisionId, overrideRequest);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Only 'APPROVED' or 'REJECTED' is permitted*");
    }

    [Fact]
    public async Task EvaluateDecisionAsync_IdempotencyRaceCondition_DetachesFailedEntityAndReplaysWinningRecord()
    {
        // Arrange: Simulate a concurrent winner inserting the same IdempotencyKey right as SaveChangesAsync is called
        var dbName = Guid.NewGuid().ToString();
        var raceKey = "race-idemp-key-001";
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 200000m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Seed winning record via a separate context so first read sees nothing, or use a custom MediatR publisher
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        using var dbContext = new TadssDbContext(options);
        var winningDecisionId = Guid.NewGuid();
        var mockPublisher = new Moq.Mock<MediatR.IPublisher>();
        mockPublisher
            .Setup(p => p.Publish(Moq.It.IsAny<TADSS.Runtime.Api.Infrastructure.Data.Auditing.DecisionEvaluatedAuditNotification>(), Moq.It.IsAny<CancellationToken>()))
            .Callback<TADSS.Runtime.Api.Infrastructure.Data.Auditing.DecisionEvaluatedAuditNotification, CancellationToken>((notif, _) =>
            {
                // Add the losing entity to ChangeTracker just like the real handler does before SaveChangesAsync fails
                dbContext.DecisionExecutions.Add(notif.Execution);

                // Simulate another concurrent transaction committing the winning record into the DB
                using var concurrentContext = new TadssDbContext(options);
                concurrentContext.DecisionExecutions.Add(new TADSS.Runtime.Api.Infrastructure.Data.Entities.DecisionExecution
                {
                    Id = Guid.NewGuid(),
                    DecisionId = winningDecisionId,
                    ReservationId = request.ReservationId,
                    StrategyName = "DefaultDecisionStrategy",
                    StrategyVersion = "1.0.0",
                    ExecutionType = "AUTOMATED",
                    ExecutionTimeMs = 4,
                    FallbackUsed = false,
                    CacheHit = false,
                    NeedApproval = false,
                    IdempotencyKey = raceKey,
                    RequestHash = notif.Execution.RequestHash,
                    ResponsePayload = System.Text.Json.JsonSerializer.Serialize(new DecisionResultDto
                    {
                        DecisionId = winningDecisionId,
                        ReservationId = request.ReservationId,
                        Status = "APPROVED",
                        StrategyName = "DefaultDecisionStrategy",
                        StrategyVersion = "1.0.0",
                        ExecutionTimeMs = 4,
                        FallbackUsed = false,
                        PrimaryReason = "AUTO_APPROVED_TIER_ELIGIBLE",
                        Reasons = [],
                        Recommendations = [],
                        EvaluatedAt = DateTime.UtcNow
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                concurrentContext.SaveChanges();

                throw new DbUpdateException("Duplicate key value violates unique constraint uq_decision_idempotency_key");
            });

        var service = new DecisionService(
            dbContext,
            CreateResolver(),
            new DecisionResiliencePipeline(new ResilienceOptions(), new FallbackDecisionStrategy(), NullLogger<DecisionResiliencePipeline>.Instance),
            CreateMemoryCacheService(),
            NullLogger<DecisionService>.Instance,
            mockPublisher.Object);

        // Act
        var replayed = await service.EvaluateDecisionAsync(request, raceKey);

        // Assert 1: Winning record is replayed
        replayed.DecisionId.Should().Be(winningDecisionId);

        // Assert 2: Losing entity was detached from ChangeTracker (no poisoned Added entity left behind)
        dbContext.ChangeTracker.Entries<TADSS.Runtime.Api.Infrastructure.Data.Entities.DecisionExecution>()
            .Where(e => e.State == EntityState.Added)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenDbUpdateConcurrencyExceptionThrown_Returns409ConflictProblemDetails()
    {
        // Arrange
        var handler = new TADSS.Runtime.Api.Middlewares.GlobalExceptionHandler(
            NullLogger<TADSS.Runtime.Api.Middlewares.GlobalExceptionHandler>.Instance);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/decisions/018e3a2b-8a71-7000-8c24-112233445566/override";
        await using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        var concurrencyEx = new DbUpdateConcurrencyException("Database operation expected to affect 1 row(s), but actually affected 0 row(s).");

        // Act
        var handled = await handler.TryHandleAsync(httpContext, concurrencyEx, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict);

        responseBody.Seek(0, SeekOrigin.Begin);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(responseBody);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Concurrency Conflict");
        doc.RootElement.GetProperty("code").GetString().Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.Conflict);
    }

    [Fact]
    public async Task EvaluateDecisionAsync_ConcurrentIdempotencyRaceWithPostgres23505AndDifferentHash_ThrowsIdempotencyConflictException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var raceKey = "race-pg23505-conflict-key";
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddHours(2),
            EndTime = DateTime.UtcNow.AddHours(4),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 200000m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        using var dbContext = new TadssDbContext(options);
        var mockPublisher = new Moq.Mock<MediatR.IPublisher>();
        mockPublisher
            .Setup(p => p.Publish(Moq.It.IsAny<TADSS.Runtime.Api.Infrastructure.Data.Auditing.DecisionEvaluatedAuditNotification>(), Moq.It.IsAny<CancellationToken>()))
            .Callback<TADSS.Runtime.Api.Infrastructure.Data.Auditing.DecisionEvaluatedAuditNotification, CancellationToken>((notif, _) =>
            {
                dbContext.DecisionExecutions.Add(notif.Execution);

                // Simulate concurrent winner committing a record with the SAME idempotency key but a DIFFERENT RequestHash
                using var concurrentContext = new TadssDbContext(options);
                concurrentContext.DecisionExecutions.Add(new TADSS.Runtime.Api.Infrastructure.Data.Entities.DecisionExecution
                {
                    Id = Guid.NewGuid(),
                    DecisionId = Guid.NewGuid(),
                    ReservationId = request.ReservationId,
                    StrategyName = "DefaultDecisionStrategy",
                    StrategyVersion = "1.0.0",
                    ExecutionType = "AUTOMATED",
                    ExecutionTimeMs = 4,
                    FallbackUsed = false,
                    CacheHit = false,
                    NeedApproval = false,
                    IdempotencyKey = raceKey,
                    RequestHash = new string('f', 64), // Different payload hash!
                    ResponsePayload = "{}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                concurrentContext.SaveChanges();

                var pgEx = new Npgsql.PostgresException(
                    messageText: "duplicate key value violates unique constraint \"uq_decision_idempotency_key\"",
                    severity: "ERROR",
                    invariantSeverity: "ERROR",
                    sqlState: "23505",
                    constraintName: "uq_decision_idempotency_key");

                throw new DbUpdateException("An error occurred while saving the entity changes.", pgEx);
            });

        var service = new DecisionService(
            dbContext,
            CreateResolver(),
            new DecisionResiliencePipeline(new ResilienceOptions(), new FallbackDecisionStrategy(), NullLogger<DecisionResiliencePipeline>.Instance),
            CreateMemoryCacheService(),
            NullLogger<DecisionService>.Instance,
            mockPublisher.Object);

        // Act
        var act = () => service.EvaluateDecisionAsync(request, raceKey);

        // Assert
        await act.Should().ThrowAsync<TADSS.Runtime.Api.Middlewares.IdempotencyConflictException>();
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenContractValidationExceptionThrown_Returns400BadRequestWithFieldErrors()
    {
        // Arrange
        var handler = new TADSS.Runtime.Api.Middlewares.GlobalExceptionHandler(
            NullLogger<TADSS.Runtime.Api.Middlewares.GlobalExceptionHandler>.Instance);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/decisions";
        await using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        var contractEx = new Shared.DecisionContractSDK.Common.ContractValidationException(
            "INVALID_PAYMENT_STATUS",
            "Invalid PaymentStatus value 'UNKNOWN_STATUS'.",
            "PaymentStatus");

        // Act
        var handled = await handler.TryHandleAsync(httpContext, contractEx, CancellationToken.None);

        // Assert
        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest);

        responseBody.Seek(0, SeekOrigin.Begin);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(responseBody);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Contract Validation Error");
        doc.RootElement.GetProperty("code").GetString().Should().Be("INVALID_PAYMENT_STATUS");
        doc.RootElement.GetProperty("errors").GetProperty("PaymentStatus")[0].GetString()
            .Should().Contain("UNKNOWN_STATUS");
    }

    private class SlowStrategyForService : Shared.DecisionContractSDK.Interfaces.IReservationDecisionStrategy
    {
        private readonly int _delayMs;
        public string StrategyName => "SlowStrategyForService";
        public string StrategyVersion => "1.0.0";

        public SlowStrategyForService(int delayMs)
        {
            _delayMs = delayMs;
        }

        public async Task<DecisionResultDto> EvaluateAsync(Shared.DecisionContractSDK.Domain.Reservation reservation, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken);
            return new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = "APPROVED",
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = _delayMs,
                FallbackUsed = false,
                PrimaryReason = "SUCCESS",
                Reasons = [],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            };
        }
    }
}
