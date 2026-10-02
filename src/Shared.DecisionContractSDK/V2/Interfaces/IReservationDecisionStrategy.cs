using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;

namespace Shared.DecisionContractSDK.V2.Interfaces;

/// <summary>
/// Hợp đồng strategy v2 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.1). Strategy phải stateless, thread-safe,
/// không I/O ngoài SDK và không đọc đồng hồ hệ thống.
/// </summary>
public interface IReservationDecisionStrategy
{
    /// <summary>Tên định danh trong registry (bằng tên class).</summary>
    string StrategyName { get; }

    /// <summary>Phiên bản SemVer.</summary>
    string StrategyVersion { get; }

    /// <summary>Chính sách cache của strategy.</summary>
    StrategyCachePolicy CachePolicy { get; }

    /// <summary>Đánh giá reservation và trả quyết định nghiệp vụ.</summary>
    /// <param name="reservation">Reservation đã qua kiểm tra bất biến.</param>
    /// <param name="context">Ngữ cảnh runtime (thời điểm đánh giá, lịch cao điểm).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken);
}
