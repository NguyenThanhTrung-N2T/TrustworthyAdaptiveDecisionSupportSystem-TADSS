namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Một trang của hàng chờ duyệt (<see cref="IDecisionQueryStore.GetPendingApprovalsAsync"/>).
/// </summary>
/// <param name="Items">Các bản ghi <c>PRIMARY</c> đang chờ duyệt của trang, sắp theo <c>approval_expires_at</c> rồi <c>decision_id</c>.</param>
/// <param name="TotalItems">Tổng số bản ghi đang chờ của chi nhánh (không phụ thuộc trang).</param>
public sealed record PendingApprovalsPage(IReadOnlyList<DecisionExecutionRecord> Items, int TotalItems);
