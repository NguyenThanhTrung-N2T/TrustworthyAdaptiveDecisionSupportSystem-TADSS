using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace Shared.DecisionContractSDK.Interfaces;

/// <summary>
/// Hợp đồng giao diện cho mọi chiến lược ra quyết định tự động trong hệ thống TADSS.
/// </summary>
public interface IReservationDecisionStrategy
{
    string StrategyName { get; }
    string StrategyVersion { get; }

    Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default);
}
