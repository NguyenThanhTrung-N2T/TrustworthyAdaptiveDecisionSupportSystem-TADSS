using TADSS.Application.Exceptions;

namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Port audit phía tra cứu quyết định (chỉ đọc), khai báo theo nhu cầu của <c>DecisionQueryService</c> (ISP);
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

    /// <summary>
    /// Lấy một trang hàng chờ duyệt của chi nhánh: bản ghi <c>PRIMARY</c> trạng thái <c>NEED_APPROVAL</c> còn hạn duyệt
    /// (<c>approval_expires_at</c> &gt; <paramref name="nowUtc"/>, so sánh nghiêm ngặt), chưa có override hay outcome và không bị
    /// bản <c>PRIMARY</c> mới hơn của cùng đơn (cùng <c>reservation_id</c> và chi nhánh) thay thế. Bản <c>SHADOW</c> không bao giờ có mặt (INV-8).
    /// Sắp theo <c>approval_expires_at</c> rồi <c>decision_id</c> nên phân trang tất định.
    /// <para>
    /// Trang và tổng là hai truy vấn riêng, không chung transaction: giữa hai lần đọc hàng chờ có thể đổi, nên <c>TotalItems</c> chỉ là
    /// số đếm tại thời điểm đọc, không bảo đảm khớp tuyệt đối với <c>Items</c>.
    /// </para>
    /// </summary>
    /// <param name="branchId">Chi nhánh cần lấy hàng chờ.</param>
    /// <param name="page">Số trang, bắt đầu từ 1.</param>
    /// <param name="pageSize">Số bản ghi mỗi trang (≥ 1).</param>
    /// <param name="nowUtc">Mốc hiện tại để loại bản đã hết hạn duyệt (lấy từ <see cref="TimeProvider"/> của caller).</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Trang kết quả và tổng số bản ghi đang chờ của chi nhánh.</returns>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    Task<PendingApprovalsPage> GetPendingApprovalsAsync(Guid branchId, int page, int pageSize, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Đếm số quyết định đang chờ duyệt của mọi chi nhánh bằng một truy vấn gộp, <b>cùng điều kiện</b> với <see cref="GetPendingApprovalsAsync"/>
    /// (bản <c>PRIMARY</c> mới nhất của mỗi đơn, <c>NEED_APPROVAL</c>, chưa override/outcome, còn hạn duyệt). Dùng cho gauge <c>tadss.pending_approvals</c>.
    /// Chi nhánh không có bản nào đang chờ không có mặt trong kết quả.
    /// </summary>
    /// <param name="nowUtc">Mốc hiện tại để loại bản đã hết hạn duyệt (lấy từ <see cref="TimeProvider"/> của caller).</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Số quyết định đang chờ theo <c>branchId</c>.</returns>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    Task<IReadOnlyDictionary<Guid, long>> CountPendingApprovalsByBranchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
