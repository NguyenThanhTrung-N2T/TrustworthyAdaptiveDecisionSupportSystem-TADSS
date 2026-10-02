namespace TADSS.Runtime.Api.Infrastructure.Data;

/// <summary>
/// Chi tiết một quyết định để tra cứu theo <c>decisionId</c>: bản ghi đánh giá kèm override và outcome nếu có
/// (mỗi quyết định có tối đa một override và một outcome — INV-5, INV-6).
/// </summary>
/// <param name="Execution">Bản ghi đánh giá (PRIMARY hoặc SHADOW — tra cứu không lọc theo role).</param>
/// <param name="Override">Quyết định ghi đè của quản lý; <c>null</c> nếu chưa có.</param>
/// <param name="Outcome">Kết cục thực tế; <c>null</c> nếu chưa có.</param>
public sealed record DecisionDetailRecord(
    DecisionExecutionRecord Execution,
    DecisionOverrideRecord? Override,
    DecisionOutcomeRecord? Outcome);
