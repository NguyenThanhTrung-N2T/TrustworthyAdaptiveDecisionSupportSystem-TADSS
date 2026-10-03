namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Kết quả của <see cref="IDecisionGovernanceStore.InsertOverrideAsync"/>.
/// </summary>
/// <param name="Stored">
/// Override đang nằm trong store: chính bản vừa ghi khi <paramref name="IsExistingWinner"/> là <c>false</c>,
/// hoặc bản của request thắng tranh chấp khi là <c>true</c>.
/// </param>
/// <param name="IsExistingWinner">
/// <c>true</c> khi insert bị chặn bởi unique <c>uq_decision_overrides_decision_id</c> và <paramref name="Stored"/> là bản đã có
/// (quyết định đã được override trước đó); caller so khóa idempotency và payload để trả lại bản đó hoặc báo xung đột.
/// </param>
public sealed record InsertOverrideResult(DecisionOverrideRecord Stored, bool IsExistingWinner);
