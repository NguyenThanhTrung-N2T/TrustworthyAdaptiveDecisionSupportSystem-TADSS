using TADSS.Application.Abstractions.Persistence;
using TADSS.Infrastructure.Persistence.Entities;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Ánh xạ thuần (không I/O, không trạng thái) giữa record của <see cref="IDecisionEvaluationStore"/>/<see cref="IDecisionQueryStore"/> và entity EF Core.
/// Là chỗ duy nhất biết cả hai phía để entity không lọt ra ngoài store. Mọi giá trị đi qua nguyên vẹn
/// (chuỗi jsonb, <c>decimal</c>, <see cref="DateTimeOffset"/>); khóa kỹ thuật <c>Id</c> để trống cho cơ sở dữ liệu sinh.
/// </summary>
internal static class DecisionAuditMapper
{
    /// <summary>Tạo entity chưa lưu từ <paramref name="record"/>; <see cref="DecisionExecution.Id"/> do cơ sở dữ liệu sinh.</summary>
    public static DecisionExecution ToEntity(DecisionExecutionRecord record) => new()
    {
        DecisionId = record.DecisionId,
        ReservationId = record.ReservationId,
        BranchId = record.BranchId,
        Role = record.Role,
        StrategyName = record.StrategyName,
        StrategyVersion = record.StrategyVersion,
        StrategyStatus = record.StrategyStatus,
        Status = record.Status,
        DepositRate = record.DepositRate,
        DepositAmount = record.DepositAmount,
        ReasonCode = record.ReasonCode,
        ExecutionType = record.ExecutionType,
        ExecutionTimeMs = record.ExecutionTimeMs,
        FallbackUsed = record.FallbackUsed,
        FailureReason = record.FailureReason,
        CacheHit = record.CacheHit,
        ApprovalExpiresAt = record.ApprovalExpiresAt,
        RequestHash = record.RequestHash,
        RequestPayload = record.RequestPayload,
        ResponsePayload = record.ResponsePayload,
        IdempotencyKey = record.IdempotencyKey,
        CorrelationId = record.CorrelationId,
        CreatedAt = record.CreatedAt,
    };

    /// <summary>Dựng record từ entity đã đọc (bỏ khóa kỹ thuật <c>Id</c>).</summary>
    public static DecisionExecutionRecord ToRecord(DecisionExecution entity) => new(
        DecisionId: entity.DecisionId,
        ReservationId: entity.ReservationId,
        BranchId: entity.BranchId,
        Role: entity.Role,
        StrategyName: entity.StrategyName,
        StrategyVersion: entity.StrategyVersion,
        StrategyStatus: entity.StrategyStatus,
        Status: entity.Status,
        DepositRate: entity.DepositRate,
        DepositAmount: entity.DepositAmount,
        ReasonCode: entity.ReasonCode,
        ExecutionType: entity.ExecutionType,
        ExecutionTimeMs: entity.ExecutionTimeMs,
        FallbackUsed: entity.FallbackUsed,
        FailureReason: entity.FailureReason,
        CacheHit: entity.CacheHit,
        ApprovalExpiresAt: entity.ApprovalExpiresAt,
        RequestHash: entity.RequestHash,
        RequestPayload: entity.RequestPayload,
        ResponsePayload: entity.ResponsePayload,
        IdempotencyKey: entity.IdempotencyKey,
        CorrelationId: entity.CorrelationId,
        CreatedAt: entity.CreatedAt);

    /// <summary>Dựng record override từ entity đã đọc.</summary>
    public static DecisionOverrideRecord ToRecord(DecisionOverride entity) => new(
        entity.DecisionId,
        entity.Action,
        entity.DepositRate,
        entity.DepositAmount,
        entity.ActorId,
        entity.Reason,
        entity.IdempotencyKey,
        entity.CreatedAt);

    /// <summary>Dựng record outcome từ entity đã đọc.</summary>
    public static DecisionOutcomeRecord ToRecord(Entities.DecisionOutcome entity) => new(
        entity.DecisionId,
        entity.Outcome,
        entity.OccurredAt,
        entity.ReportedAt);
}
