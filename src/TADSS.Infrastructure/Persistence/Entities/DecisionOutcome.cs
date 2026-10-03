namespace TADSS.Infrastructure.Persistence.Entities;

/// <summary>
/// Kết cục thực tế của một quyết định, ánh xạ 1-1 với <c>tadss.decision_outcomes</c>
/// (DECISION_PERSISTENCE_INVARIANTS B.3). Append-only: mỗi quyết định có tối đa một outcome (INV-6).
/// </summary>
public class DecisionOutcome
{
    /// <summary>Khóa kỹ thuật, do cơ sở dữ liệu sinh.</summary>
    public Guid Id { get; set; }

    /// <summary>Quyết định có kết cục (unique, khóa ngoại tới <c>decision_executions.decision_id</c>).</summary>
    public Guid DecisionId { get; set; }

    /// <summary>Kết cục thực tế.</summary>
    public Shared.DecisionContractSDK.Enums.DecisionOutcome Outcome { get; set; }

    /// <summary>Thời điểm kết cục xảy ra (UTC, offset 0).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Thời điểm client báo kết cục (UTC, offset 0).</summary>
    public DateTimeOffset ReportedAt { get; set; }
}
