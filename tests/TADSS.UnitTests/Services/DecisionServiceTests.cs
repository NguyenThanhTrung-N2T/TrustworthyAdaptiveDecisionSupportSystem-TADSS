using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
