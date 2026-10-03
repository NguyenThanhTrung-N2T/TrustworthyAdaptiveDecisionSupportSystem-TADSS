namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Kết quả của <see cref="IDecisionEvaluationStore.InsertExecutionAsync"/>.
/// </summary>
/// <param name="Stored">
/// Bản ghi đang nằm trong store: chính bản vừa ghi khi <paramref name="IsExistingWinner"/> là <c>false</c>,
/// hoặc bản của request thắng tranh chấp khi là <c>true</c>.
/// </param>
/// <param name="IsExistingWinner">
/// <c>true</c> khi insert bị chặn bởi unique <c>uq_decision_idempotency_key</c> và <paramref name="Stored"/> là bản đã có
/// (request đồng thời cùng idempotency key thắng); caller phải so <c>RequestHash</c> rồi trả lại bản đó hoặc báo xung đột.
/// </param>
public sealed record InsertExecutionResult(DecisionExecutionRecord Stored, bool IsExistingWinner);
