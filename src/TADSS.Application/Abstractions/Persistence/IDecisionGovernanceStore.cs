using TADSS.Application.Exceptions;

namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Port audit phía quản trị quyết định (ghi override và outcome, bảng <c>tadss.decision_overrides</c>/<c>tadss.decision_outcomes</c>),
/// khai báo theo nhu cầu của use case override/outcome (ISP); hiện thực EF Core/PostgreSQL là <c>EfDecisionGovernanceStore</c>
/// (đăng ký scoped). Việc đọc (hàng chờ duyệt, chi tiết) thuộc <see cref="IDecisionQueryStore"/>.
/// <para>
/// <b>Append-only (INV-4):</b> chỉ có thêm mới; không có method sửa hay xóa, và hiện thực không được tạo ra <c>UPDATE</c>/<c>DELETE</c>.
/// </para>
/// <para>
/// <b>Phân loại lỗi mà hiện thực phải tuân thủ:</b> lỗi kết nối, timeout và lỗi PostgreSQL có thể thử lại
/// → <see cref="PersistenceUnavailableException"/> (503); mọi vi phạm ràng buộc khác (CHECK, FK, unique khác…) → để exception gốc
/// lan ra (500). Hủy của caller lan ra dưới dạng <see cref="OperationCanceledException"/>.
/// </para>
/// </summary>
public interface IDecisionGovernanceStore
{
    /// <summary>
    /// Thêm một override. Khi vi phạm unique <c>uq_decision_overrides_decision_id</c> (SqlState <c>23505</c> <b>và</b> đúng tên constraint đó),
    /// hiện thực <b>không ném</b> mà đọc lại bản đã có và trả <see cref="InsertOverrideResult.IsExistingWinner"/> là <c>true</c> (INV-5).
    /// </summary>
    /// <param name="record">Override cần thêm; <see cref="DecisionOverrideRecord.DecisionId"/> phải là quyết định đã tồn tại (khóa ngoại).</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Override đang nằm trong store và cờ cho biết đó là bản đã có từ trước.</returns>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    Task<InsertOverrideResult> InsertOverrideAsync(DecisionOverrideRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Thêm một outcome. Khi vi phạm unique <c>uq_decision_outcomes_decision_id</c> (SqlState <c>23505</c> <b>và</b> đúng tên constraint đó),
    /// hiện thực <b>không ném</b> mà đọc lại bản đã có và trả <see cref="InsertOutcomeResult.IsExistingWinner"/> là <c>true</c> (INV-6).
    /// </summary>
    /// <param name="record">Outcome cần thêm; <see cref="DecisionOutcomeRecord.DecisionId"/> phải là quyết định đã tồn tại (khóa ngoại).</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Outcome đang nằm trong store và cờ cho biết đó là bản đã có từ trước.</returns>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    Task<InsertOutcomeResult> InsertOutcomeAsync(DecisionOutcomeRecord record, CancellationToken cancellationToken);
}
