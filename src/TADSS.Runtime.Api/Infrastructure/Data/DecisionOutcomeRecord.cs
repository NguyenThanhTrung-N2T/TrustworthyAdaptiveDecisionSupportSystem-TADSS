using Shared.DecisionContractSDK.Enums;

namespace TADSS.Runtime.Api.Infrastructure.Data;

/// <summary>
/// Kết cục thực tế của một quyết định đã lưu ở <c>tadss.decision_outcomes</c> (DECISION_PERSISTENCE_INVARIANTS B.3), dạng chỉ đọc
/// cho <see cref="DecisionDetailRecord"/>.
/// </summary>
/// <param name="DecisionId">Quyết định có kết cục.</param>
/// <param name="Outcome">Kết cục thực tế.</param>
/// <param name="OccurredAt">Thời điểm kết cục xảy ra (UTC, offset 0).</param>
/// <param name="ReportedAt">Thời điểm client báo kết cục (UTC, offset 0).</param>
public sealed record DecisionOutcomeRecord(
    Guid DecisionId,
    DecisionOutcome Outcome,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReportedAt);
