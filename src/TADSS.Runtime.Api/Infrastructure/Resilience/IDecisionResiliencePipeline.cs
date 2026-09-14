using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Giao diện Pipeline chịu lỗi đa tầng (Nested Resilience Pipeline) bao bọc việc thực thi Decision Strategy.
/// </summary>
public interface IDecisionResiliencePipeline
{
    /// <summary>
    /// Thực thi Decision Strategy bên trong chuỗi bảo vệ (Outer Timeout -> Fallback -> Retry -> Circuit Breaker -> Inner Timeout).
    /// </summary>
    Task<DecisionExecutionOutcome> ExecuteStrategyAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        CancellationToken cancellationToken = default);
}
