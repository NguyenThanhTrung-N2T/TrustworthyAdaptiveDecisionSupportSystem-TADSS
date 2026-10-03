using Shared.DecisionContractSDK.Enums;

namespace TADSS.Infrastructure.Persistence.Entities;

/// <summary>
/// Quyết định ghi đè của quản lý, ánh xạ 1-1 với <c>tadss.decision_overrides</c> (DECISION_PERSISTENCE_INVARIANTS B.2).
/// Append-only: mỗi quyết định có tối đa một override (INV-5) và không bao giờ bị sửa.
/// </summary>
public class DecisionOverride
{
    /// <summary>Khóa kỹ thuật, do cơ sở dữ liệu sinh.</summary>
    public Guid Id { get; set; }

    /// <summary>Quyết định bị ghi đè (unique, khóa ngoại tới <c>decision_executions.decision_id</c>).</summary>
    public Guid DecisionId { get; set; }

    /// <summary>Hành động của quản lý: <c>APPROVE</c> hoặc <c>REJECT</c>.</summary>
    public OverrideAction Action { get; set; }

    /// <summary>Tỷ lệ cọc; chỉ có khi <see cref="Action"/> là <c>APPROVE</c>.</summary>
    public decimal? DepositRate { get; set; }

    /// <summary>Số tiền cọc; chỉ có khi <see cref="Action"/> là <c>APPROVE</c>.</summary>
    public decimal? DepositAmount { get; set; }

    /// <summary>Người thực hiện override.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Lý do override (5–500 ký tự).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Khóa idempotency của lần override.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Thời điểm ghi override (UTC, offset 0).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
