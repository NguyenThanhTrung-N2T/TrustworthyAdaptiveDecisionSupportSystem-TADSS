using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;

namespace TADSS.Application.Decisions;

/// <summary>
/// Log tóm tắt của một quyết định đã được ghi audit (template và mức log giữ nguyên). Tách khỏi <see cref="DecisionService"/> để service
/// chỉ còn điều phối; logger vẫn là logger của <see cref="DecisionService"/> nên category không đổi. Không log payload hay dữ liệu khách hàng.
/// </summary>
internal static class DecisionLogging
{
    /// <summary>Ghi log kết luận của quyết định (kèm dòng riêng khi approval window không khả dụng, B-05).</summary>
    public static void LogDecisionSummary(
        this ILogger logger,
        DecisionExecutionRecord record,
        StrategyExecutionOutcome outcome,
        IReservationDecisionStrategy strategy,
        string idempotencyKey,
        string? correlationId)
    {
        // B-05: approval window không khả dụng → ghi reasonCode gốc của strategy (response chỉ còn lý do cố định)
        if (record.StrategyStatus == DecisionStatus.NEED_APPROVAL && record.Status == DecisionStatus.APPROVED)
        {
            logger.LogInformation(
                "Approval window không khả dụng cho quyết định {DecisionId}: strategy {StrategyName} trả {StrategyStatus} với reasonCode gốc {StrategyReasonCode}; chuyển APPROVED cọc 100%. IdempotencyKey {IdempotencyKey}.",
                record.DecisionId,
                strategy.StrategyName,
                record.StrategyStatus,
                outcome.Decision.ReasonCode,
                idempotencyKey);
        }

        logger.LogInformation(
            "Quyết định {DecisionId} cho reservation {ReservationId}: {Status} ({ReasonCode}), strategy {StrategyName} v{StrategyVersion}, executionType {ExecutionType}, fallbackUsed {FallbackUsed}, {ElapsedMs} ms, idempotencyKey {IdempotencyKey}, correlationId {CorrelationId}.",
            record.DecisionId,
            record.ReservationId,
            record.Status,
            record.ReasonCode,
            record.StrategyName,
            record.StrategyVersion,
            record.ExecutionType,
            record.FallbackUsed,
            record.ExecutionTimeMs,
            idempotencyKey,
            correlationId);
    }
}
