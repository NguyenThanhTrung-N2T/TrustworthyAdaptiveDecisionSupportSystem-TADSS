using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>Telemetry thực thi của một quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExecutionDetailDto
{
    /// <summary>Tên strategy.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Phiên bản strategy.</summary>
    public required string StrategyVersion { get; init; }

    /// <summary>Cách quyết định được tạo ra.</summary>
    public required ExecutionType ExecutionType { get; init; }

    /// <summary>Thời gian thực thi (ms).</summary>
    public required int ExecutionTimeMs { get; init; }

    /// <summary>Có dùng fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Lý do lỗi khi fallback (nếu có).</summary>
    public required string? FailureReason { get; init; }

    /// <summary>Lấy từ cache.</summary>
    public required bool CacheHit { get; init; }
}
