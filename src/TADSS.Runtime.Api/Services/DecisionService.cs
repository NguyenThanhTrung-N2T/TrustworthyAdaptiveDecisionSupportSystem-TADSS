using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Services;

public class DecisionService : IDecisionService
{
    private readonly TadssDbContext _dbContext;
    private readonly IReservationDecisionStrategy _activeStrategy;
    private readonly IDecisionResiliencePipeline _resiliencePipeline;
    private readonly IDecisionCacheService _cacheService;
    private readonly ILogger<DecisionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DecisionService(
        TadssDbContext dbContext,
        IReservationDecisionStrategy activeStrategy,
        IDecisionResiliencePipeline resiliencePipeline,
        IDecisionCacheService cacheService,
        ILogger<DecisionService> logger)
    {
        _dbContext = dbContext;
        _activeStrategy = activeStrategy;
        _resiliencePipeline = resiliencePipeline;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<DecisionResultDto> EvaluateDecisionAsync(
        DecisionRequestDto request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var requestHash = RequestHasher.ComputeHash(request);

        // 1. Kiểm tra Idempotency (Task 4.2 - Same transaction replay, 0 new audit records)
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingExecution = await _dbContext.DecisionExecutions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existingExecution != null)
            {
                if (existingExecution.RequestHash == requestHash)
                {
                    _logger.LogInformation("Idempotency match for key {IdempotencyKey}. Replaying decision {DecisionId}.",
                        idempotencyKey, existingExecution.DecisionId);

                    var replayedResult = JsonSerializer.Deserialize<DecisionResultDto>(
                        existingExecution.ResponsePayload, JsonOptions);

                    return replayedResult ?? throw new InvalidOperationException("Failed to deserialize replayed decision payload.");
                }

                _logger.LogWarning("Idempotency conflict: Key {IdempotencyKey} reused with different payload hash.", idempotencyKey);
                throw new IdempotencyConflictException(idempotencyKey,
                    $"The provided X-Idempotency-Key '{idempotencyKey}' has already been used with a different request payload.");
            }
        }

        // 2. Kiểm tra Distributed Caching Layer (Task 4.3 - Same payload, different key -> CACHE_HIT audit record)
        var cacheKey = $"decision:hash:{requestHash}:v{_activeStrategy.StrategyVersion}";
        var cachedResult = await _cacheService.GetAsync(cacheKey, cancellationToken);
        if (cachedResult != null)
        {
            _logger.LogInformation("Cache hit for request hash {RequestHash} with strategy version {StrategyVersion}.",
                requestHash, _activeStrategy.StrategyVersion);

            var newDecisionId = Guid.NewGuid();
            var cacheHitResult = new DecisionResultDto
            {
                DecisionId = newDecisionId,
                ReservationId = request.ReservationId,
                Status = cachedResult.Status,
                StrategyName = cachedResult.StrategyName,
                StrategyVersion = cachedResult.StrategyVersion,
                ExecutionTimeMs = 2,
                FallbackUsed = false,
                PrimaryReason = cachedResult.PrimaryReason,
                Reasons = cachedResult.Reasons,
                Recommendations = cachedResult.Recommendations,
                EvaluatedAt = DateTime.UtcNow
            };

            var cacheHitPayload = JsonSerializer.Serialize(cacheHitResult, JsonOptions);

            var cacheHitExecution = new DecisionExecution
            {
                DecisionId = newDecisionId,
                ReservationId = request.ReservationId,
                StrategyName = cacheHitResult.StrategyName,
                StrategyVersion = cacheHitResult.StrategyVersion,
                ExecutionType = ExecutionType.CACHE_HIT.ToString(),
                ExecutionTimeMs = cacheHitResult.ExecutionTimeMs,
                FallbackUsed = false,
                FailureReason = null,
                CacheHit = true,
                NeedApproval = cacheHitResult.Status == DecisionStatus.NEED_APPROVAL.ToString(),
                RequestHash = requestHash,
                ResponsePayload = cacheHitPayload,
                IdempotencyKey = idempotencyKey,
                IsOverridden = false
            };

            return await PersistDecisionAsync(cacheHitExecution, idempotencyKey, cacheHitResult, cancellationToken);
        }

        // 3. Map DTO sang Domain Model (Tự động kích hoạt Domain Invariant validation)
        var reservation = request.ToDomain();

        // 4. Thực thi Strategy thông qua chuỗi Resilience Pipeline (Outer Timeout -> Fallback -> Retry -> Circuit Breaker -> Inner Timeout)
        var outcome = await _resiliencePipeline.ExecuteStrategyAsync(_activeStrategy, reservation, cancellationToken);
        var result = outcome.Result;

        // 5. Lưu kết quả vào Cache (CHỈ cache khi kết quả là AUTOMATED và KHÔNG dùng Fallback)
        if (outcome.ExecutionType == ExecutionType.AUTOMATED && !outcome.FallbackUsed)
        {
            await _cacheService.SetAsync(cacheKey, result, cancellationToken: cancellationToken);
        }

        // 6. Lưu vết Audit vào bảng tadss.decision_executions (với cơ chế bắt race condition)
        var jsonPayload = JsonSerializer.Serialize(result, JsonOptions);

        var executionEntity = new DecisionExecution
        {
            DecisionId = result.DecisionId,
            ReservationId = result.ReservationId,
            StrategyName = result.StrategyName,
            StrategyVersion = result.StrategyVersion,
            ExecutionType = outcome.ExecutionType.ToString(),
            ExecutionTimeMs = outcome.ExecutionTimeMs,
            FallbackUsed = outcome.FallbackUsed,
            FailureReason = outcome.FailureReason,
            CacheHit = false,
            NeedApproval = result.Status == DecisionStatus.NEED_APPROVAL.ToString(),
            RequestHash = requestHash,
            ResponsePayload = jsonPayload,
            IdempotencyKey = idempotencyKey,
            IsOverridden = false
        };

        return await PersistDecisionAsync(executionEntity, idempotencyKey, result, cancellationToken);
    }

    private async Task<DecisionResultDto> PersistDecisionAsync(
        DecisionExecution executionEntity,
        string? idempotencyKey,
        DecisionResultDto evaluatedResult,
        CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.DecisionExecutions.Add(executionEntity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Decision {DecisionId} evaluated for Reservation {ReservationId} with Status {Status} in {ElapsedMs}ms (ExecutionType: {ExecutionType}, FallbackUsed: {FallbackUsed}).",
                evaluatedResult.DecisionId, evaluatedResult.ReservationId, evaluatedResult.Status, evaluatedResult.ExecutionTimeMs, executionEntity.ExecutionType, executionEntity.FallbackUsed);

            return evaluatedResult;
        }
        catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(idempotencyKey) && IsIdempotencyConstraintViolation(ex))
        {
            _logger.LogWarning("Idempotency race condition detected for key {IdempotencyKey}. Re-checking database for winning record.", idempotencyKey);

            var existingExecution = await _dbContext.DecisionExecutions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existingExecution == null)
            {
                throw new InvalidOperationException($"Idempotency record disappeared during race condition handling for key '{idempotencyKey}'.");
            }

            if (existingExecution.RequestHash == executionEntity.RequestHash)
            {
                _logger.LogInformation("Race condition resolved: replaying result from concurrent request for key {IdempotencyKey}.", idempotencyKey);
                return DeserializeResult(existingExecution.ResponsePayload);
            }

            _logger.LogWarning("Idempotency conflict detected during race condition for key {IdempotencyKey}.", idempotencyKey);
            throw new IdempotencyConflictException(idempotencyKey,
                $"The idempotency key '{idempotencyKey}' was already used by a concurrent request with a different payload.");
        }
    }

    private static bool IsIdempotencyConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Npgsql.PostgresException pgEx)
        {
            return pgEx.SqlState == "23505" &&
                   (pgEx.ConstraintName == "uq_decision_idempotency_key" ||
                    pgEx.ConstraintName == "uq_decision_idempotency" ||
                    pgEx.ConstraintName == "uq_decision_executions_idempotency_key" ||
                    pgEx.Message.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase));
        }

        return ex.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase) ||
               ex.InnerException?.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static DecisionResultDto DeserializeResult(string responsePayload)
    {
        var result = JsonSerializer.Deserialize<DecisionResultDto>(responsePayload, JsonOptions);
        return result ?? throw new InvalidOperationException("Failed to deserialize decision result from audit record.");
    }

    public async Task<DecisionDetailDto?> GetDecisionDetailAsync(
        Guid decisionId,
        CancellationToken cancellationToken = default)
    {
        var execution = await _dbContext.DecisionExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DecisionId == decisionId, cancellationToken);

        if (execution == null)
        {
            return null;
        }

        var resultDto = JsonSerializer.Deserialize<DecisionResultDto>(
            execution.ResponsePayload, JsonOptions);

        return new DecisionDetailDto
        {
            DecisionId = execution.DecisionId,
            ReservationId = execution.ReservationId,
            RequestHash = execution.RequestHash,
            Decision = new DecisionSummaryDto
            {
                Status = resultDto?.Status ?? (execution.NeedApproval ? DecisionStatus.NEED_APPROVAL.ToString() : DecisionStatus.APPROVED.ToString()),
                PrimaryReason = resultDto?.PrimaryReason ?? string.Empty,
                Reasons = resultDto?.Reasons ?? [],
                Recommendations = resultDto?.Recommendations ?? []
            },
            Execution = new ExecutionSummaryDto
            {
                StrategyName = execution.StrategyName,
                StrategyVersion = execution.StrategyVersion,
                ExecutionType = execution.ExecutionType,
                ExecutionTimeMs = execution.ExecutionTimeMs,
                FallbackUsed = execution.FallbackUsed,
                FailureReason = execution.FailureReason,
                CacheHit = execution.CacheHit
            },
            Governance = new GovernanceSummaryDto
            {
                NeedApproval = execution.NeedApproval,
                IsOverridden = execution.IsOverridden,
                OverriddenBy = execution.OverriddenBy,
                OverrideReason = execution.OverrideReason,
                OverriddenAt = execution.IsOverridden ? execution.UpdatedAt : null
            },
            Timestamps = new TimestampSummaryDto
            {
                CreatedAt = execution.CreatedAt,
                UpdatedAt = execution.UpdatedAt
            }
        };
    }

    public async Task<PagedResult<PendingApprovalItemDto>> GetPendingApprovalsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.DecisionExecutions
            .AsNoTracking()
            .Where(e => e.NeedApproval && !e.IsOverridden);

        var totalItems = await query.CountAsync(cancellationToken);

        var executions = await query
            .OrderBy(e => e.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var items = executions.Select(e =>
        {
            var dto = JsonSerializer.Deserialize<DecisionResultDto>(
                e.ResponsePayload, JsonOptions);

            return new PendingApprovalItemDto
            {
                DecisionId = e.DecisionId,
                ReservationId = e.ReservationId,
                Status = DecisionStatus.NEED_APPROVAL.ToString(),
                PrimaryReason = dto?.PrimaryReason ?? (e.FailureReason ?? ErrorCodes.DepositBelowThreshold),
                StrategyName = e.StrategyName,
                StrategyVersion = e.StrategyVersion,
                ExecutionTimeMs = e.ExecutionTimeMs,
                FallbackUsed = e.FallbackUsed,
                CreatedAt = e.CreatedAt
            };
        }).ToList();

        return new PagedResult<PendingApprovalItemDto>
        {
            Page = safePage,
            PageSize = safePageSize,
            TotalItems = totalItems,
            Items = items
        };
    }

    public async Task<OverrideDecisionResponseDto?> OverrideDecisionAsync(
        Guid decisionId,
        OverrideDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var execution = await _dbContext.DecisionExecutions
            .FirstOrDefaultAsync(e => e.DecisionId == decisionId, cancellationToken);

        if (execution == null)
        {
            return null;
        }

        if (!execution.NeedApproval)
        {
            throw new InvalidOperationException("This decision is not eligible for override because it did not require approval.");
        }

        if (execution.IsOverridden)
        {
            throw new InvalidOperationException(
                $"This decision has already been overridden by '{execution.OverriddenBy}' " +
                $"at {execution.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC. Reason: {execution.OverrideReason}");
        }

        var resultDto = JsonSerializer.Deserialize<DecisionResultDto>(
            execution.ResponsePayload, JsonOptions);

        var originalStatus = resultDto?.Status ?? DecisionStatus.NEED_APPROVAL.ToString();

        // Invariant 3: Không sửa response_payload gốc của máy, chỉ cập nhật audit governance flags
        execution.IsOverridden = true;
        execution.OverriddenBy = request.OverriddenBy;
        execution.OverrideReason = request.OverrideReason;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Decision {DecisionId} overridden by {OverriddenBy} from {OriginalStatus} to {NewStatus}.",
            decisionId, request.OverriddenBy, originalStatus, request.Decision.Status);

        return new OverrideDecisionResponseDto
        {
            DecisionId = execution.DecisionId,
            ReservationId = execution.ReservationId,
            OriginalStatus = originalStatus,
            OverriddenStatus = request.Decision.Status,
            IsOverridden = true,
            OverriddenBy = request.OverriddenBy,
            OverrideReason = request.OverrideReason,
            OverriddenAt = execution.UpdatedAt
        };
    }
}
