using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Decisions;

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

/// <summary>Một item của request.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReservationItemDto
{
    /// <summary>Định danh tài nguyên.</summary>
    public required Guid ResourceId { get; init; }

    /// <summary>Định danh loại tài nguyên.</summary>
    public required Guid ResourceTypeId { get; init; }

    /// <summary>Thời điểm bắt đầu (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>Thời điểm kết thúc (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset EndTime { get; init; }

    /// <summary>Giá của item (SmashCourt: phí sân trước giảm giá).</summary>
    public required decimal Price { get; init; }
}

/// <summary>Thông tin khách của request.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerDto
{
    /// <summary>Định danh khách; bắt buộc khi <see cref="IsGuest"/> là <c>false</c>, <c>null</c> khi là khách vãng lai.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Khách vãng lai.</summary>
    public required bool IsGuest { get; init; }

    /// <summary>Hạng thành viên chuẩn hóa.</summary>
    public required CustomerTier Tier { get; init; }

    /// <summary>Thời điểm tạo tài khoản (tùy chọn, có offset); <c>null</c> với khách vãng lai.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public DateTimeOffset? MemberSince { get; init; }

    /// <summary>Lịch sử đặt chỗ (tùy chọn).</summary>
    public CustomerHistoryDto? History { get; init; }
}

/// <summary>Lịch sử đặt chỗ của khách (INTEGRATION_SPEC §3.3).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerHistoryDto
{
    /// <summary>Số booking đã hoàn tất.</summary>
    public required int CompletedCount { get; init; }

    /// <summary>Số lần không đến.</summary>
    public required int NoShowCount { get; init; }

    /// <summary>Số lần khách tự hủy và được hoàn đủ.</summary>
    public required int CancelledCount { get; init; }

    /// <summary>Số lần hủy muộn (tùy chọn, mặc định 0).</summary>
    public int LateCancelledCount { get; init; }

    /// <summary>Số lần hết hạn thanh toán (tùy chọn, mặc định 0).</summary>
    public int PaymentExpiredCount { get; init; }

    /// <summary>Thời điểm hoàn tất gần nhất (tùy chọn, có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public DateTimeOffset? LastCompletedAt { get; init; }
}

/// <summary>Ảnh chụp nhu cầu tại khung giờ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DemandDto
{
    /// <summary>Tỷ lệ kín sân trong <c>[0, 1]</c>.</summary>
    public required decimal OccupancyRate { get; init; }

    /// <summary>Số người chờ slot.</summary>
    public required int WaitlistCount { get; init; }
}
