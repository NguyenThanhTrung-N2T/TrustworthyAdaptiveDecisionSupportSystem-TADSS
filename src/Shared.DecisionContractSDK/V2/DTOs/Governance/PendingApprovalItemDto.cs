using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Governance;

/// <summary>Một mục trong hàng chờ duyệt (INTEGRATION_SPEC §3.5).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PendingApprovalItemDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Định danh chi nhánh.</summary>
    public required Guid BranchId { get; init; }

    /// <summary>Tổng tiền.</summary>
    public required decimal TotalPrice { get; init; }

    /// <summary>Mã tiền tệ.</summary>
    public required string Currency { get; init; }

    /// <summary>Thời điểm bắt đầu sớm nhất.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset EarliestStart { get; init; }

    /// <summary>Thông tin khách rút gọn.</summary>
    public required PendingApprovalCustomerDto Customer { get; init; }

    /// <summary>Mã lý do cần duyệt.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Quyết định đến từ fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Hạn duyệt.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset ApprovalExpiresAt { get; init; }

    /// <summary>Thời điểm tạo quyết định.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Thông tin khách rút gọn trong hàng chờ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PendingApprovalCustomerDto
{
    /// <summary>Khách vãng lai.</summary>
    public required bool IsGuest { get; init; }

    /// <summary>Hạng thành viên.</summary>
    public required CustomerTier Tier { get; init; }
}
