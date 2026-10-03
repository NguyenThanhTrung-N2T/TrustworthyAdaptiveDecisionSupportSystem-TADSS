using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Strategies;

namespace TADSS.Application.Decisions;

/// <summary>
/// Đầu vào để ghép một quyết định thành response và bản ghi audit.
/// </summary>
/// <param name="DecisionId">Định danh quyết định do runtime cấp.</param>
/// <param name="Request">Request gốc (được serialize nguyên vẹn vào <c>request_payload</c>).</param>
/// <param name="Reservation">Reservation đã dựng từ request.</param>
/// <param name="Strategy">Strategy đã được chọn (tên/phiên bản đi vào response và audit, kể cả khi fallback — B-04).</param>
/// <param name="Outcome">Kết quả chạy strategy qua pipeline.</param>
/// <param name="CacheHit">Quyết định của strategy lấy từ cache.</param>
/// <param name="NowUtc">Thời điểm đánh giá, lấy một lần cho cả request (B-03).</param>
/// <param name="RequestHash">Hash của request đã chuẩn hóa.</param>
/// <param name="IdempotencyKey">Khóa idempotency của request.</param>
/// <param name="CorrelationId">Mã tương quan; <c>null</c> khi không có.</param>
public sealed record DecisionComposition(
    Guid DecisionId,
    DecisionRequestDto Request,
    Reservation Reservation,
    RegisteredStrategy Strategy,
    StrategyExecutionOutcome Outcome,
    bool CacheHit,
    DateTimeOffset NowUtc,
    string RequestHash,
    string IdempotencyKey,
    string? CorrelationId);
