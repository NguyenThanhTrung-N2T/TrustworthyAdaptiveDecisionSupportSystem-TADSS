using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Interfaces;

namespace TADSS.Runtime.Api.V2.Infrastructure.Resilience;

/// <summary>
/// Chạy strategy v2 trong lớp bảo vệ chịu lỗi (timeout, retry, circuit breaker, fallback).
/// </summary>
public interface IStrategyPipeline
{
    /// <summary>
    /// Chạy <paramref name="strategy"/>; lỗi của strategy được chuyển thành quyết định fallback thay vì lan ra ngoài.
    /// </summary>
    /// <param name="strategy">Strategy cần chạy.</param>
    /// <param name="reservation">Reservation cần đánh giá.</param>
    /// <param name="context">Ngữ cảnh runtime của lần đánh giá.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Kết quả <c>AUTOMATED</c> hoặc <c>FALLBACK</c>.</returns>
    /// <exception cref="OperationCanceledException">Khi chính <paramref name="cancellationToken"/> bị hủy (không fallback).</exception>
    /// <exception cref="Polly.Timeout.TimeoutRejectedException">Khi vượt Outer timeout (ngân sách toàn pipeline).</exception>
    Task<StrategyExecutionOutcome> ExecuteAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        DecisionContext context,
        CancellationToken cancellationToken);
}
