using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Auditing;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Services;

public class DecisionService : IDecisionService
{
    private readonly TadssDbContext _dbContext;
    private readonly IStrategyResolver _strategyResolver;
    private readonly IDecisionResiliencePipeline _resiliencePipeline;
    private readonly IDecisionCacheService _cacheService;
    private readonly ILogger<DecisionService> _logger;
    private readonly IPublisher? _publisher;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DecisionService(
        TadssDbContext dbContext,
        IStrategyResolver strategyResolver,
        IDecisionResiliencePipeline resiliencePipeline,
        IDecisionCacheService cacheService,
        ILogger<DecisionService> logger,
        IPublisher? publisher = null)
    {
        _dbContext = dbContext;
        _strategyResolver = strategyResolver;
        _resiliencePipeline = resiliencePipeline;
        _cacheService = cacheService;
        _logger = logger;
        _publisher = publisher;
    }

    public async Task<DecisionResultDto> EvaluateDecisionAsync(
        DecisionRequestDto request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var requestHash = RequestHasher.ComputeHash(request);

        // 1. Kiểm tra Idempotency (Task 4.2 - Same transaction replay, 0 new audit records)
        // Invariant: Idempotency MUST trump configuration changes — runs before strategy resolution
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
                    $"The provided {HttpHeaderNames.IdempotencyKey} '{idempotencyKey}' has already been used with a different request payload.");
            }
        }

        // 2. Phân giải Strategy đang hoạt động theo chính sách triển khai (Task 3.2 - O(1) Lookup)
        var activeStrategy = _strategyResolver.ResolveActive();

        // 3. Kiểm tra Distributed Caching Layer (scoped theo StrategyName và StrategyVersion)
        var cacheKey = CacheKeyBuilder.BuildDecisionHashKey(requestHash, activeStrategy.StrategyName, activeStrategy.StrategyVersion);
        DecisionResultDto? cachedResult = null;
        var cacheStopwatch = Stopwatch.StartNew();
        try
        {
            cachedResult = await _cacheService.GetAsync(cacheKey, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected cache read error for key {CacheKey}. Treating as cache miss.", cacheKey);
        }
        finally
        {
            cacheStopwatch.Stop();
        }

        if (cachedResult != null)
        {
            var cacheElapsedMs = (int)cacheStopwatch.ElapsedMilliseconds;

            _logger.LogInformation("Cache hit for request hash {RequestHash} with strategy {StrategyName} v{StrategyVersion} in {ElapsedMs}ms.",
                requestHash, activeStrategy.StrategyName, activeStrategy.StrategyVersion, cacheElapsedMs);

            var newDecisionId = Guid.NewGuid();
            var cacheHitResult = new DecisionResultDto
            {
                DecisionId = newDecisionId,
                ReservationId = request.ReservationId,
                Status = cachedResult.Status,
                StrategyName = cachedResult.StrategyName,
                StrategyVersion = cachedResult.StrategyVersion,
                ExecutionTimeMs = cacheElapsedMs,
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

        // 4. Map DTO sang Domain Model (Tự động kích hoạt Domain Invariant validation)
        var reservation = request.ToDomain();

        // 5. Thực thi Strategy thông qua chuỗi Resilience Pipeline (Outer Timeout -> Fallback -> Retry -> Circuit Breaker -> Inner Timeout)
        var outcome = await _resiliencePipeline.ExecuteStrategyAsync(activeStrategy, reservation, cancellationToken);
        var result = outcome.Result;

        // 6. Lưu kết quả vào Cache (CHỈ cache khi kết quả là AUTOMATED và KHÔNG dùng Fallback)
        if (outcome.ExecutionType == ExecutionType.AUTOMATED && !outcome.FallbackUsed)
        {
            try
            {
                await _cacheService.SetAsync(cacheKey, result, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected cache write error for key {CacheKey}. Proceeding without caching.", cacheKey);
            }
        }

        // 7. Lưu vết Audit vào bảng tadss.decision_executions (với cơ chế bắt race condition)
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
            var auditNotification = new DecisionEvaluatedAuditNotification(executionEntity);
            if (_publisher != null)
            {
                await _publisher.Publish(auditNotification, cancellationToken);
            }
            else
            {
                var handler = new DecisionEvaluatedAuditNotificationHandler(_dbContext);
                await handler.Handle(auditNotification, cancellationToken);
            }

            _logger.LogInformation("Decision {DecisionId} evaluated for Reservation {ReservationId} with Status {Status} in {ElapsedMs}ms (ExecutionType: {ExecutionType}, FallbackUsed: {FallbackUsed}).",
                evaluatedResult.DecisionId, evaluatedResult.ReservationId, evaluatedResult.Status, evaluatedResult.ExecutionTimeMs, executionEntity.ExecutionType, executionEntity.FallbackUsed);

            return evaluatedResult;
        }
        catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(idempotencyKey) && IsIdempotencyConstraintViolation(ex))
        {
            _dbContext.Entry(executionEntity).State = EntityState.Detached;
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
        var targetStatus = request.Decision?.Status?.Trim().ToUpperInvariant();
        if (targetStatus is not (nameof(DecisionStatus.APPROVED) or nameof(DecisionStatus.REJECTED)))
        {
            throw new ArgumentException(
                $"Invalid override target status '{request.Decision?.Status}'. Only '{DecisionStatus.APPROVED}' or '{DecisionStatus.REJECTED}' is permitted.",
                nameof(request));
        }

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

        // Invariant 3: Không sửa response_payload gốc của máy, chỉ cập nhật audit governance flags qua MediatR in-process event
        var overrideNotification = new DecisionOverriddenAuditNotification(
            execution,
            request.OverriddenBy,
            request.OverrideReason);

        if (_publisher != null)
        {
            await _publisher.Publish(overrideNotification, cancellationToken);
        }
        else
        {
            var handler = new DecisionOverriddenAuditNotificationHandler(_dbContext);
            await handler.Handle(overrideNotification, cancellationToken);
        }

        _logger.LogInformation("Decision {DecisionId} overridden by {OverriddenBy} from {OriginalStatus} to {NewStatus}.",
            decisionId, request.OverriddenBy, originalStatus, targetStatus);

        return new OverrideDecisionResponseDto
        {
            DecisionId = execution.DecisionId,
            ReservationId = execution.ReservationId,
            OriginalStatus = originalStatus,
            OverriddenStatus = targetStatus,
            IsOverridden = true,
            OverriddenBy = request.OverriddenBy,
            OverrideReason = request.OverrideReason,
            OverriddenAt = execution.UpdatedAt
        };
    }
}
