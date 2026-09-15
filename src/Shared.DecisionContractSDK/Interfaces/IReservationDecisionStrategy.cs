using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace Shared.DecisionContractSDK.Interfaces;

/// <summary>
/// Hợp đồng giao diện cho mọi chiến lược ra quyết định tự động trong hệ thống TADSS.
/// </summary>
public interface IReservationDecisionStrategy
{
    /// <summary>
    /// Tên định danh của chiến lược ra quyết định.
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Phiên bản của chiến lược ra quyết định (SemVer, ví dụ "1.0.0").
    /// </summary>
    string StrategyVersion { get; }

    /// <summary>
    /// Thẩm định một yêu cầu đặt sân và sinh ra kết quả quyết định.
    /// </summary>
    /// <param name="reservation">Thực thể đặt sân trong miền nghiệp vụ.</param>
    /// <param name="cancellationToken">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Đối tượng DecisionResultDto chứa kết quả thẩm định.</returns>
    Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default);
}
