using System.Text.Json;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Decisions;

namespace TADSS.Application.Governance;

/// <summary>
/// Ánh xạ thuần từ bản ghi audit sang DTO của override, outcome và hàng chờ duyệt. Dùng chung cho use case ghi (override, outcome)
/// và use case đọc (chi tiết, hàng chờ) để cùng một bản ghi luôn ra cùng một response (replay trùng bản gốc).
/// </summary>
internal static class GovernanceResponseMapper
{
    /// <summary>
    /// Dựng response override. Bảng <c>decision_overrides</c> không có cột tiền tệ nên <paramref name="currency"/> lấy từ request của quyết định.
    /// </summary>
    public static OverrideResponseDto ToResponse(DecisionOverrideRecord record, string currency) => new()
    {
        DecisionId = record.DecisionId,
        Action = record.Action,
        // ck_do_deposit: rate và amount cùng có ⇔ APPROVE
        Deposit = record is { DepositRate: { } rate, DepositAmount: { } amount }
            ? new DepositDto { Rate = rate, Amount = amount, Currency = currency }
            : null,
        ActorId = record.ActorId,
        Reason = record.Reason,
        OverriddenAt = record.CreatedAt.ToUniversalTime()
    };

    /// <summary>Dựng response outcome.</summary>
    public static OutcomeResponseDto ToResponse(DecisionOutcomeRecord record) => new()
    {
        DecisionId = record.DecisionId,
        Outcome = record.Outcome,
        OccurredAt = record.OccurredAt,
        ReportedAt = record.ReportedAt.ToUniversalTime()
    };

    /// <summary>
    /// Dựng một mục hàng chờ: các cột lấy từ bản ghi, tổng tiền/tiền tệ/khách/<c>earliestStart</c> đọc từ <c>request_payload</c> bằng options đọc nới lỏng.
    /// <c>earliestStart</c> là <c>startTime</c> sớm nhất theo thời điểm, giữ nguyên offset của item đó.
    /// </summary>
    /// <exception cref="JsonException">Khi payload hỏng hoặc thiếu item/khách (lỗi dữ liệu, không bị che).</exception>
    public static PendingApprovalItemDto ToPendingItem(DecisionExecutionRecord record)
    {
        var request = DecisionPayloadSerializer.DeserializeRequest(record.RequestPayload);
        if (request.Items is not { Count: > 0 } items || request.Customer is null)
        {
            throw new JsonException("request_payload của bản ghi audit thiếu items hoặc customer.");
        }

        // Min theo thời điểm (DateTimeOffset so sánh theo UTC) nhưng trả nguyên giá trị của item đó nên giữ offset
        var earliestStart = items.Min(item => item.StartTime);

        return new PendingApprovalItemDto
        {
            DecisionId = record.DecisionId,
            ReservationId = record.ReservationId,
            BranchId = record.BranchId,
            TotalPrice = request.TotalPrice,
            Currency = request.Currency,
            EarliestStart = earliestStart,
            Customer = new PendingApprovalCustomerDto { IsGuest = request.Customer.IsGuest, Tier = request.Customer.Tier },
            ReasonCode = record.ReasonCode,
            FallbackUsed = record.FallbackUsed,
            // ck_de_approval: NEED_APPROVAL luôn có hạn duyệt; hàng chờ chỉ chứa NEED_APPROVAL
            ApprovalExpiresAt = (record.ApprovalExpiresAt
                ?? throw new InvalidOperationException("Bản ghi hàng chờ không có approval_expires_at.")).ToUniversalTime(),
            CreatedAt = record.CreatedAt.ToUniversalTime()
        };
    }
}
