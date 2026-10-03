using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Strategies;

namespace TADSS.Application.Decisions;

/// <summary>
/// Ghép quyết định của strategy, approval window và mức cọc thành <see cref="DecisionResultDto"/> cùng
/// <see cref="DecisionExecutionRecord"/> (INTEGRATION_SPEC §3.3, DECISION_PERSISTENCE_INVARIANTS B.1). Là phép biến đổi thuần:
/// thời điểm là tham số, response và bản ghi được dựng từ cùng một kết quả nên không thể lệch nhau, và INV-7
/// (<c>APPROVED</c> ⇔ có mức cọc, <c>NEED_APPROVAL</c> ⇔ có hạn duyệt) đúng ở cả hai.
/// </summary>
public sealed class DecisionResponseComposer
{
    /// <summary>Số chữ số thập phân tối đa của tỷ lệ cọc (C-09).</summary>
    public const int MaxRateDecimals = 4;

    private readonly DepositCalculator _depositCalculator;
    private readonly ApprovalWindowPolicy _approvalWindowPolicy;

    /// <summary>
    /// Khởi tạo bộ ghép với bộ quy đổi cọc và chính sách approval window.
    /// </summary>
    public DecisionResponseComposer(DepositCalculator depositCalculator, ApprovalWindowPolicy approvalWindowPolicy)
    {
        ArgumentNullException.ThrowIfNull(depositCalculator);
        ArgumentNullException.ThrowIfNull(approvalWindowPolicy);

        _depositCalculator = depositCalculator;
        _approvalWindowPolicy = approvalWindowPolicy;
    }

    /// <summary>
    /// Ghép response và bản ghi audit cho một lần đánh giá.
    /// </summary>
    /// <param name="composition">Đầu vào đã được orchestrator chuẩn bị.</param>
    /// <returns>Response và bản ghi <c>PRIMARY</c> tương ứng.</returns>
    public ComposedDecision Compose(DecisionComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var reservation = composition.Reservation;
        var outcome = composition.Outcome;
        var nowUtc = composition.NowUtc.ToUniversalTime();

        var window = _approvalWindowPolicy.Apply(outcome.Decision, reservation.EarliestStart, nowUtc);
        var deposit = window.Status == DecisionStatus.APPROVED
            ? _depositCalculator.Calculate(
                reservation.TotalPrice,
                EnsureRepresentableRate(window.DepositRate ?? throw new InvalidOperationException("Quyết định APPROVED phải có tỷ lệ cọc.")),
                reservation.Currency)
            : null;

        // Kết quả lấy từ cache không phải fallback và không có lỗi (kể cả khi outcome mang dấu hiệu fallback)
        var executionType = composition.CacheHit ? ExecutionType.CACHE_HIT : outcome.ExecutionType;
        var fallbackUsed = !composition.CacheHit && outcome.FallbackUsed;
        var failureReason = composition.CacheHit ? null : outcome.FailureReason;

        var strategy = composition.Strategy.Strategy;
        var response = new DecisionResultDto
        {
            DecisionId = composition.DecisionId,
            ReservationId = reservation.ReservationId,
            Status = window.Status,
            Deposit = deposit,
            StrategyName = strategy.StrategyName,
            StrategyVersion = strategy.StrategyVersion,
            ExecutionType = executionType,
            FallbackUsed = fallbackUsed,
            ReasonCode = window.ReasonCode,
            Reasons = window.Reasons,
            Recommendations = window.Recommendations
                .Select(recommendation => new DecisionRecommendationDto
                {
                    Type = recommendation.Type,
                    Description = recommendation.Description
                })
                .ToArray(),
            ApprovalExpiresAt = window.ApprovalExpiresAt,
            EvaluatedAt = nowUtc
        };

        var record = new DecisionExecutionRecord(
            DecisionId: composition.DecisionId,
            ReservationId: reservation.ReservationId,
            BranchId: reservation.BranchId,
            Role: ExecutionRole.PRIMARY,
            StrategyName: response.StrategyName,
            StrategyVersion: response.StrategyVersion,
            StrategyStatus: window.StrategyStatus,
            Status: response.Status,
            DepositRate: deposit?.Rate,
            DepositAmount: deposit?.Amount,
            ReasonCode: response.ReasonCode,
            ExecutionType: executionType,
            ExecutionTimeMs: outcome.ExecutionTimeMs,
            FallbackUsed: fallbackUsed,
            FailureReason: failureReason,
            CacheHit: composition.CacheHit,
            ApprovalExpiresAt: response.ApprovalExpiresAt,
            RequestHash: composition.RequestHash,
            RequestPayload: DecisionPayloadSerializer.SerializeRequest(composition.Request),
            ResponsePayload: DecisionPayloadSerializer.SerializeResponse(response),
            IdempotencyKey: composition.IdempotencyKey,
            CorrelationId: composition.CorrelationId,
            CreatedAt: nowUtc);

        return new ComposedDecision(response, record);
    }

    /// <summary>
    /// Tỷ lệ cọc đi ra wire tối đa <see cref="MaxRateDecimals"/> chữ số thập phân (C-09). Strategy trả tỷ lệ nhiều chữ số hơn
    /// là lỗi lập trình/cấu hình: ném lỗi để không ghi audit thay vì làm tròn âm thầm làm lệch tỷ lệ so với quyết định.
    /// </summary>
    private static decimal EnsureRepresentableRate(decimal rate)
    {
        if (decimal.Round(rate, MaxRateDecimals) != rate)
        {
            throw new InvalidOperationException(
                $"Tỷ lệ cọc {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)} có quá {MaxRateDecimals} chữ số thập phân; " +
                $"quyết định APPROVED chỉ được mang tỷ lệ tối đa {MaxRateDecimals} chữ số thập phân.");
        }

        return rate;
    }
}
