using Shared.DecisionContractSDK.Enums;

namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Một bản ghi bất biến của <c>tadss.decision_executions</c> (DECISION_PERSISTENCE_INVARIANTS B.1), đủ mọi cột trừ
/// <c>id</c> (khóa kỹ thuật do cơ sở dữ liệu sinh). Ràng buộc <c>ck_de_deposit</c>/<c>ck_de_approval</c> (INV-7):
/// <see cref="DepositRate"/> và <see cref="DepositAmount"/> cùng có ⇔ <see cref="Status"/> là <c>APPROVED</c>;
/// <see cref="ApprovalExpiresAt"/> có ⇔ <see cref="Status"/> là <c>NEED_APPROVAL</c>.
/// </summary>
/// <param name="DecisionId">Định danh quyết định do runtime cấp (<c>decision_id</c>, unique — INV-1).</param>
/// <param name="ReservationId">Định danh đơn phía client.</param>
/// <param name="BranchId">Định danh chi nhánh (lọc hàng chờ duyệt).</param>
/// <param name="Role">Vai trò bản ghi.</param>
/// <param name="StrategyName">Tên strategy đã được chọn (kể cả khi fallback — B-04).</param>
/// <param name="StrategyVersion">Phiên bản strategy đã được chọn.</param>
/// <param name="StrategyStatus">Kết quả gốc của strategy/fallback, trước khi áp approval window.</param>
/// <param name="Status">Trạng thái cuối cùng trả cho client.</param>
/// <param name="DepositRate">Tỷ lệ cọc; chỉ có khi <paramref name="Status"/> là <c>APPROVED</c>.</param>
/// <param name="DepositAmount">Số tiền cọc đã quy đổi; chỉ có khi <paramref name="Status"/> là <c>APPROVED</c>.</param>
/// <param name="ReasonCode">Mã lý do cuối cùng (≤ 64 ký tự).</param>
/// <param name="ExecutionType">Cách quyết định được tạo ra.</param>
/// <param name="ExecutionTimeMs">Thời gian chạy strategy qua pipeline (ms, không âm).</param>
/// <param name="FallbackUsed">Có dùng fallback hay không.</param>
/// <param name="FailureReason">Lý do máy đọc được khi fallback; <c>null</c> nếu không.</param>
/// <param name="CacheHit">Quyết định của strategy lấy từ cache.</param>
/// <param name="ApprovalExpiresAt">Hạn duyệt (UTC); chỉ có khi <paramref name="Status"/> là <c>NEED_APPROVAL</c>.</param>
/// <param name="RequestHash">SHA-256 hex thường (64 ký tự) của request đã chuẩn hóa (INV-3).</param>
/// <param name="RequestPayload">Request v2 đã serialize bằng <c>TadssJson.Options</c> (cột jsonb).</param>
/// <param name="ResponsePayload">Response v2 đã serialize bằng <c>TadssJson.Options</c> (cột jsonb).</param>
/// <param name="IdempotencyKey">Khóa idempotency; <c>null</c> với bản ghi không thuộc luồng idempotency.</param>
/// <param name="CorrelationId">Mã tương quan của request; <c>null</c> khi không có.</param>
/// <param name="CreatedAt">Thời điểm đánh giá (UTC, offset 0) — cùng giá trị với <c>evaluatedAt</c> của response (B-03).</param>
public sealed record DecisionExecutionRecord(
    Guid DecisionId,
    Guid ReservationId,
    Guid BranchId,
    ExecutionRole Role,
    string StrategyName,
    string StrategyVersion,
    DecisionStatus StrategyStatus,
    DecisionStatus Status,
    decimal? DepositRate,
    decimal? DepositAmount,
    string ReasonCode,
    ExecutionType ExecutionType,
    int ExecutionTimeMs,
    bool FallbackUsed,
    string? FailureReason,
    bool CacheHit,
    DateTimeOffset? ApprovalExpiresAt,
    string RequestHash,
    string RequestPayload,
    string ResponsePayload,
    string? IdempotencyKey,
    string? CorrelationId,
    DateTimeOffset CreatedAt);
