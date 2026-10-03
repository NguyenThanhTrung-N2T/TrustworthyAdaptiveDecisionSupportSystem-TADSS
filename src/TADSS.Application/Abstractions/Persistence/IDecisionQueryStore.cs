using TADSS.Application.Exceptions;

namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Port audit phía tra cứu quyết định (chỉ đọc), khai báo theo nhu cầu của <c>DecisionQueryService</c> (ISP — E-07);
/// hiện thực là <c>EfDecisionAuditStore</c> (đăng ký scoped, dùng chung instance với <see cref="IDecisionEvaluationStore"/>).
/// Giữ ràng buộc append-only (INV-4): không có method sửa hay xóa và không lộ DbContext/<c>IQueryable</c>/entity EF.
/// Lỗi lưu trữ tạm thời được dịch thành <see cref="PersistenceUnavailableException"/> như <see cref="IDecisionEvaluationStore"/>.
/// </summary>
public interface IDecisionQueryStore
{
    /// <summary>
    /// Tra chi tiết một quyết định theo <c>decisionId</c>: bản ghi đánh giá kèm override và outcome nếu có.
    /// Tra theo <c>decision_id</c> nên <b>không lọc theo role</b> (cả <c>PRIMARY</c> lẫn <c>SHADOW</c>).
    /// </summary>
    /// <param name="decisionId">Định danh quyết định do runtime cấp.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Chi tiết quyết định, hoặc <c>null</c> nếu không có bản ghi đánh giá nào với <paramref name="decisionId"/>.</returns>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    Task<DecisionDetailRecord?> GetDecisionDetailAsync(Guid decisionId, CancellationToken cancellationToken);
}
