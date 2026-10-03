using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>Ảnh chụp nhu cầu tại khung giờ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DemandDto
{
    /// <summary>Tỷ lệ kín sân trong <c>[0, 1]</c>.</summary>
    public required decimal OccupancyRate { get; init; }

    /// <summary>Số người chờ slot.</summary>
    public required int WaitlistCount { get; init; }
}
