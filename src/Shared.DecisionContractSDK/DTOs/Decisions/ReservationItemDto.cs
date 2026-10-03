using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

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
