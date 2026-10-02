using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Kết quả chạy một strategy v2 qua pipeline chịu lỗi.
/// </summary>
/// <param name="Decision">Quyết định của strategy, hoặc quyết định fallback khi strategy không dùng được.</param>
/// <param name="ExecutionType"><c>AUTOMATED</c> khi strategy trả kết quả, <c>FALLBACK</c> khi dùng quyết định dự phòng.</param>
/// <param name="FallbackUsed">Có dùng fallback hay không.</param>
/// <param name="FailureReason">Lý do máy đọc được (tiền tố <c>FailureReasonPrefixes</c>) khi fallback; <c>null</c> khi thành công.</param>
/// <param name="ExecutionTimeMs">Thời gian chạy toàn pipeline (ms, làm tròn xuống, không âm).</param>
public sealed record StrategyExecutionOutcome(
    StrategyDecision Decision,
    ExecutionType ExecutionType,
    bool FallbackUsed,
    string? FailureReason,
    int ExecutionTimeMs);
