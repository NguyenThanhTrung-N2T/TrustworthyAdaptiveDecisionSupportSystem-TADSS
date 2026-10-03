using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace TADSS.Application.Decisions;

/// <summary>
/// Quyết định sau khi áp quy tắc approval window.
/// </summary>
/// <param name="Status">Trạng thái cuối cùng trả cho client.</param>
/// <param name="StrategyStatus">Trạng thái do strategy (hoặc fallback) quyết định, trước khi áp approval window.</param>
/// <param name="DepositRate">Tỷ lệ cọc; chỉ có khi <paramref name="Status"/> là <c>APPROVED</c>.</param>
/// <param name="ReasonCode">Mã lý do cuối cùng.</param>
/// <param name="Reasons">Lý do cho người đọc.</param>
/// <param name="Recommendations">Khuyến nghị kèm theo.</param>
/// <param name="ApprovalExpiresAt">Hạn duyệt (UTC); chỉ có khi <paramref name="Status"/> là <c>NEED_APPROVAL</c>.</param>
/// <param name="WindowUnavailable">
/// <c>true</c> khi <c>NEED_APPROVAL</c> bị chuyển thành <c>APPROVED</c> cọc 100% vì approval window không khả dụng.
/// </param>
public sealed record ApprovalWindowResult(
    DecisionStatus Status,
    DecisionStatus StrategyStatus,
    decimal? DepositRate,
    string ReasonCode,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<DecisionRecommendation> Recommendations,
    DateTimeOffset? ApprovalExpiresAt,
    bool WindowUnavailable);
