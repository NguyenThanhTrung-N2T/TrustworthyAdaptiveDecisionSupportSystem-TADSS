using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;

namespace TADSS.Application.Decisions;

/// <summary>
/// Log tóm tắt của một quyết định đã được ghi audit (mức log giữ nguyên; template có thêm <c>{RuleName}</c>, A-04). Tách khỏi <see cref="DecisionService"/> để service
/// chỉ còn điều phối; logger vẫn là logger của <see cref="DecisionService"/> nên category không đổi. Không log payload hay dữ liệu khách hàng.
/// </summary>
internal static class DecisionLogging
{
    /// <summary>Giá trị <c>RuleName</c> trong log khi không rule định tuyến nào khớp (dùng strategy mặc định).</summary>
    internal const string DefaultRuleName = "default";

    /// <summary>
    /// Ghi log kết luận của quyết định (kèm dòng riêng khi approval window không khả dụng, B-05). <paramref name="ruleName"/> là rule định tuyến đã khớp
    /// (A-04; <c>null</c> ghi là <see cref="DefaultRuleName"/>) — chỉ có trong log, không lưu DB, tra lại theo <c>decisionId</c>.
    /// </summary>
    public static void LogDecisionSummary(
        this ILogger logger,
        DecisionExecutionRecord record,
        StrategyExecutionOutcome outcome,
        IReservationDecisionStrategy strategy,
        string? ruleName,
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
            "Quyết định {DecisionId} cho reservation {ReservationId}: {Status} ({ReasonCode}), strategy {StrategyName} v{StrategyVersion}, rule {RuleName}, executionType {ExecutionType}, fallbackUsed {FallbackUsed}, {ElapsedMs} ms, idempotencyKey {IdempotencyKey}, correlationId {CorrelationId}.",
            record.DecisionId,
            record.ReservationId,
            record.Status,
            record.ReasonCode,
            record.StrategyName,
            record.StrategyVersion,
            ruleName ?? DefaultRuleName,
            record.ExecutionType,
            record.FallbackUsed,
            record.ExecutionTimeMs,
            idempotencyKey,
            correlationId);
    }
}
