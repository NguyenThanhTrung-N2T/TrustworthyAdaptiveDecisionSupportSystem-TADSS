using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Options;

namespace TADSS.Application.Decisions;

/// <summary>
/// Quy tắc approval window (INTEGRATION_SPEC §1.1): hạn duyệt là <c>min(now + WindowMinutes, earliestStart − MinLeadMinutes)</c>.
/// Nếu hạn đó không còn ở tương lai thì không thể chờ Manager duyệt, quyết định chuyển thành <c>APPROVED</c> với cọc 100%.
/// Thời điểm hiện tại là tham số (không đọc đồng hồ) và mọi phép tính thực hiện trên instant.
/// </summary>
public sealed class ApprovalWindowPolicy
{
    private const string WindowUnavailableReason =
        "Không còn đủ thời gian để Manager duyệt trước giờ bắt đầu, nên áp dụng mức cọc 100% thay cho bước duyệt.";

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
