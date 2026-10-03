using Shared.DecisionContractSDK.Enums;

namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Quyết định ghi đè của quản lý đã lưu ở <c>tadss.decision_overrides</c> (DECISION_PERSISTENCE_INVARIANTS B.2), dạng chỉ đọc
/// cho <see cref="DecisionDetailRecord"/>. Ràng buộc <c>ck_do_deposit</c>: <see cref="DepositRate"/> và <see cref="DepositAmount"/>
/// cùng có ⇔ <see cref="Action"/> là <c>APPROVE</c>.
/// </summary>
/// <param name="DecisionId">Quyết định bị ghi đè.</param>
/// <param name="Action">Hành động của quản lý (<c>APPROVE</c> hoặc <c>REJECT</c>).</param>
/// <param name="DepositRate">Tỷ lệ cọc; chỉ có khi <paramref name="Action"/> là <c>APPROVE</c>.</param>
/// <param name="DepositAmount">Số tiền cọc; chỉ có khi <paramref name="Action"/> là <c>APPROVE</c>.</param>
/// <param name="ActorId">Người thực hiện override.</param>
/// <param name="Reason">Lý do override (5–500 ký tự).</param>
/// <param name="IdempotencyKey">Khóa idempotency của lần override.</param>
/// <param name="CreatedAt">Thời điểm ghi override (UTC, offset 0).</param>
public sealed record DecisionOverrideRecord(
    Guid DecisionId,
    OverrideAction Action,
    decimal? DepositRate,
    decimal? DepositAmount,
    string ActorId,
    string Reason,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);
