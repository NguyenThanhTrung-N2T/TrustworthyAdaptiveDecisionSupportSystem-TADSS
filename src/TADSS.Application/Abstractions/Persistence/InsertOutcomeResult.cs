namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Kết quả của <see cref="IDecisionGovernanceStore.InsertOutcomeAsync"/>.
/// </summary>
/// <param name="Stored">
/// Outcome đang nằm trong store: chính bản vừa ghi khi <paramref name="IsExistingWinner"/> là <c>false</c>,
/// hoặc bản đã có khi là <c>true</c>.
/// </param>
/// <param name="IsExistingWinner">
/// <c>true</c> khi insert bị chặn bởi unique <c>uq_decision_outcomes_decision_id</c> và <paramref name="Stored"/> là bản đã có
/// (quyết định đã có kết cục trước đó); caller so payload để trả lại bản đó hoặc báo xung đột.
/// </param>
public sealed record InsertOutcomeResult(DecisionOutcomeRecord Stored, bool IsExistingWinner);
