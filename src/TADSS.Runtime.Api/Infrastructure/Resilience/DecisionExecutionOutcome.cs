using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Kết quả thực thi chiến lược quyết định được bao đóng cùng metadata chịu lỗi (Resilience Execution Telemetry).
/// </summary>
public record DecisionExecutionOutcome
{
    /// <summary>
    /// Kết quả quyết định kinh doanh chuẩn.
    /// </summary>
    public required DecisionResultDto Result { get; init; }

    /// <summary>
    /// Loại thực thi: AUTOMATED hoặc FALLBACK.
    /// </summary>
    public required ExecutionType ExecutionType { get; init; }

    /// <summary>
    /// Đánh dấu xem chế độ an toàn Fallback có được kích hoạt hay không.
    /// </summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>
    /// Lý do chi tiết hoặc mã lỗi nếu chiến lược chính thất bại và rơi vào Fallback.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Tổng thời gian thực thi (milliseconds) của toàn bộ Resilience Pipeline.
    /// </summary>
    public int ExecutionTimeMs { get; init; }
}
