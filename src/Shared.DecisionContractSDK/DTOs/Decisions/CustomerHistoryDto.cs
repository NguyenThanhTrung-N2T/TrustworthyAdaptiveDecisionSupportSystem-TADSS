using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

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
