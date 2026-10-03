using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.Decisions;

/// <summary>
/// Quyết định nghiệp vụ do strategy trả về (D-03). Strategy chỉ quyết định <i>cái gì</i>; runtime lo định danh,
/// thời gian, quy đổi số tiền cọc, approval window và telemetry. Chỉ tạo qua <see cref="Approve"/>,
/// <see cref="RequireApproval"/>, <see cref="Reject"/>.
/// </summary>
public sealed class StrategyDecision
{
    /// <summary>Độ dài tối đa của mã lý do (cột <c>reason_code</c>).</summary>
    public const int MaxReasonCodeLength = 64;

    /// <summary>Trạng thái quyết định.</summary>
    public DecisionStatus Status { get; }

    /// <summary>Tỷ lệ cọc trong <c>[0, 1]</c>; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public decimal? DepositRate { get; }

    /// <summary>Mã lý do (xem <c>ReasonCodes</c>).</summary>
    public string ReasonCode { get; }

    /// <summary>Lý do cho người đọc.</summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public IReadOnlyList<DecisionRecommendation> Recommendations { get; }

    private StrategyDecision(
        DecisionStatus status,
        decimal? depositRate,
        string reasonCode,
        IReadOnlyList<string> reasons,
        IReadOnlyList<DecisionRecommendation> recommendations)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > MaxReasonCodeLength)
        {
            throw new DomainInvariantException("INVALID_REASON_CODE", $"ReasonCode must be 1..{MaxReasonCodeLength} characters.", nameof(ReasonCode));
        }

        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(recommendations);

        if (reasons.Any(reason => reason is null))
        {
            throw new DomainInvariantException("INVALID_REASONS", "Reasons must not contain null elements.", nameof(Reasons));
        }

        if (recommendations.Any(recommendation => recommendation is null))
        {
            throw new DomainInvariantException("INVALID_RECOMMENDATIONS", "Recommendations must not contain null elements.", nameof(Recommendations));
        }

        Status = status;
        DepositRate = depositRate;
        ReasonCode = reasonCode;
        Reasons = Array.AsReadOnly(reasons.ToArray());
        Recommendations = Array.AsReadOnly(recommendations.ToArray());
    }

    /// <summary>Duyệt với tỷ lệ cọc <paramref name="depositRate"/>.</summary>
    /// <param name="depositRate">Tỷ lệ cọc trong <c>[0, 1]</c>.</param>
    /// <param name="reasonCode">Mã lý do.</param>
    /// <param name="reasons">Lý do cho người đọc.</param>
    /// <exception cref="DomainInvariantException">Khi tỷ lệ ngoài <c>[0, 1]</c> hoặc mã lý do không hợp lệ.</exception>
    public static StrategyDecision Approve(decimal depositRate, string reasonCode, IReadOnlyList<string> reasons)
    {
        if (depositRate is < 0m or > 1m)
        {
            throw new DomainInvariantException("INVALID_DEPOSIT_RATE", $"DepositRate ({depositRate}) must be between 0 and 1.", nameof(DepositRate));
        }

        return new StrategyDecision(DecisionStatus.APPROVED, depositRate, reasonCode, reasons, []);
    }

    /// <summary>Yêu cầu Manager duyệt.</summary>
    /// <param name="reasonCode">Mã lý do.</param>
    /// <param name="reasons">Lý do cho người đọc.</param>
    /// <param name="recommendations">Khuyến nghị kèm theo (tùy chọn).</param>
    public static StrategyDecision RequireApproval(
        string reasonCode,
        IReadOnlyList<string> reasons,
        IReadOnlyList<DecisionRecommendation>? recommendations = null) =>
        new(DecisionStatus.NEED_APPROVAL, null, reasonCode, reasons, recommendations ?? []);

    /// <summary>Từ chối theo chính sách.</summary>
    /// <param name="reasonCode">Mã lý do.</param>
    /// <param name="reasons">Lý do cho người đọc.</param>
    public static StrategyDecision Reject(string reasonCode, IReadOnlyList<string> reasons) =>
        new(DecisionStatus.REJECTED, null, reasonCode, reasons, []);
}
