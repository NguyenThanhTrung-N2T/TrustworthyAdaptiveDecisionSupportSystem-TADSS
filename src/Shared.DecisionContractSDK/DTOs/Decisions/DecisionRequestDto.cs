using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Request <c>POST /api/v2/decisions</c> (INTEGRATION_SPEC §3.3). Thiếu trường bắt buộc, trường lạ, enum lạ
/// hoặc thời gian thiếu offset làm deserialize thất bại (400 <c>VALIDATION_ERROR</c>).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionRequestDto
{
    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Định danh chi nhánh.</summary>
    public required Guid BranchId { get; init; }

    /// <summary>Mã múi giờ IANA của chi nhánh.</summary>
    public required string BranchTimeZone { get; init; }

    /// <summary>Các item (1..20).</summary>
    public required IReadOnlyList<ReservationItemDto> Items { get; init; }

    /// <summary>Thông tin khách.</summary>
    public required CustomerDto Customer { get; init; }

    /// <summary>Tổng tiền sau giảm giá.</summary>
    public required decimal TotalPrice { get; init; }

    /// <summary>Mã tiền tệ ISO 4217.</summary>
    public required string Currency { get; init; }

    /// <summary>Kênh tạo booking.</summary>
    public required Channel Channel { get; init; }

    /// <summary>Ảnh chụp nhu cầu tại khung giờ (tùy chọn; không thuộc <c>request_hash</c>).</summary>
    public DemandDto? Demand { get; init; }

    /// <summary>Thời điểm client gửi yêu cầu (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Metadata không ảnh hưởng quyết định (tùy chọn).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
