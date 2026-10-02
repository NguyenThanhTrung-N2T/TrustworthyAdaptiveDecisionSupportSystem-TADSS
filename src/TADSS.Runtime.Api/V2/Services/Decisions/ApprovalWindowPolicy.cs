using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.Runtime.Api.V2.Configuration;

namespace TADSS.Runtime.Api.V2.Services.Decisions;

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

/// <summary>
/// Quy tắc approval window (INTEGRATION_SPEC §1.1): hạn duyệt là <c>min(now + WindowMinutes, earliestStart − MinLeadMinutes)</c>.
/// Nếu hạn đó không còn ở tương lai thì không thể chờ Manager duyệt, quyết định chuyển thành <c>APPROVED</c> với cọc 100%.
/// Thời điểm hiện tại là tham số (không đọc đồng hồ) và mọi phép tính thực hiện trên instant.
/// </summary>
public sealed class ApprovalWindowPolicy
{
    private const string WindowUnavailableReason =
        "Không còn đủ thời gian để Manager duyệt trước giờ bắt đầu, nên đơn được duyệt tự động với mức cọc 100%.";

    private readonly TimeSpan _window;
    private readonly TimeSpan _minLead;

    /// <summary>
    /// Khởi tạo chính sách từ cấu hình <c>Tadss:Approval</c> (đã được kiểm tra khi khởi động).
    /// </summary>
    public ApprovalWindowPolicy(IOptions<ApprovalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _window = TimeSpan.FromMinutes(options.Value.WindowMinutes);
        _minLead = TimeSpan.FromMinutes(options.Value.MinLeadMinutes);
    }

    /// <summary>
    /// Áp approval window lên quyết định của strategy hoặc fallback.
    /// </summary>
    /// <param name="decision">Quyết định của strategy/fallback.</param>
    /// <param name="earliestStart">Giờ bắt đầu sớm nhất trong các item của reservation.</param>
    /// <param name="nowUtc">Thời điểm đánh giá (một lần cho mỗi request).</param>
    /// <returns>Quyết định cuối cùng; luôn thỏa INV-7 (<c>APPROVED</c> ⇔ có mức cọc, <c>NEED_APPROVAL</c> ⇔ có hạn duyệt).</returns>
    public ApprovalWindowResult Apply(StrategyDecision decision, DateTimeOffset earliestStart, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Status != DecisionStatus.NEED_APPROVAL)
        {
            return new ApprovalWindowResult(
                decision.Status,
                decision.Status,
                decision.DepositRate,
                decision.ReasonCode,
                decision.Reasons,
                decision.Recommendations,
                ApprovalExpiresAt: null,
                WindowUnavailable: false);
        }

        var now = nowUtc.ToUniversalTime();
        var byWindow = now + _window;
        var byLead = earliestStart.ToUniversalTime() - _minLead;
        var expires = byWindow < byLead ? byWindow : byLead;

        if (expires > now)
        {
            return new ApprovalWindowResult(
                DecisionStatus.NEED_APPROVAL,
                DecisionStatus.NEED_APPROVAL,
                DepositRate: null,
                decision.ReasonCode,
                decision.Reasons,
                decision.Recommendations,
                expires,
                WindowUnavailable: false);
        }

        return new ApprovalWindowResult(
            DecisionStatus.APPROVED,
            DecisionStatus.NEED_APPROVAL,
            DepositRate: 1m,
            ReasonCodes.ApprovalWindowUnavailable,
            [WindowUnavailableReason],
            [],
            ApprovalExpiresAt: null,
            WindowUnavailable: true);
    }
}
