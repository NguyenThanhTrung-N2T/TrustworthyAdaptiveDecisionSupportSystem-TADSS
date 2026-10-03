using TADSS.Application.Decisions;
using TADSS.Application.Exceptions;

namespace TADSS.Application.Abstractions.Persistence;

/// <summary>
/// Port audit phía đánh giá quyết định (bảng <c>tadss.decision_executions</c>), khai báo theo nhu cầu của <c>DecisionService</c>
/// (B-09, ISP — E-07); hiện thực EF Core/PostgreSQL là <c>EfDecisionAuditStore</c> (đăng ký scoped, dùng chung instance với
/// <see cref="IDecisionQueryStore"/>).
/// <para>
/// <b>Append-only (INV-4):</b> chỉ có đọc (<see cref="FindPrimaryByIdempotencyKeyAsync"/>) và thêm mới
/// (<see cref="InsertExecutionAsync"/>); không có method sửa hay xóa, và hiện thực không được tạo ra
/// <c>UPDATE</c>/<c>DELETE</c> lên bảng (trigger <c>trg_de_append_only</c> sẽ từ chối).
/// </para>
/// <para>
/// <b>Phân loại lỗi mà hiện thực phải tuân thủ:</b> lỗi kết nối, timeout và lỗi PostgreSQL có thể thử lại
/// → ném <see cref="PersistenceUnavailableException"/> (503); mọi vi phạm ràng buộc khác (CHECK, FK, unique
/// khác <c>uq_decision_idempotency_key</c>…) → để exception gốc lan ra (500, không phải 503). Hủy của caller
/// (<see cref="OperationCanceledException"/>) lan ra nguyên vẹn.
/// </para>
/// </summary>
public interface IDecisionEvaluationStore
{
    /// <summary>
    /// Tìm bản ghi <c>PRIMARY</c> đã lưu cho một idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">Khóa idempotency của request.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Bản ghi <c>PRIMARY</c> có khóa đó, hoặc <c>null</c> nếu chưa có.</returns>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    Task<DecisionExecutionRecord?> FindPrimaryByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Thêm một bản ghi mới.
    /// <para>
    /// Khi vi phạm unique <c>uq_decision_idempotency_key</c> (PostgreSQL SqlState <c>23505</c> <b>và</b> đúng tên
    /// constraint đó — unique khác như <c>decision_id</c> không được coi là tranh chấp idempotency), hiện thực
    /// <b>không ném</b> mà đọc lại bản đã thắng và trả <see cref="InsertExecutionResult"/> với
    /// <see cref="InsertExecutionResult.IsExistingWinner"/> là <c>true</c>.
    /// </para>
    /// </summary>
    /// <param name="record">Bản ghi cần thêm; <see cref="DecisionExecutionRecord.DecisionId"/> do runtime cấp.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Bản ghi đang nằm trong store và cờ cho biết đó là bản thắng đã có từ trước.</returns>
    /// <exception cref="PersistenceUnavailableException">
    /// Khi lỗi kết nối, timeout hoặc lỗi PostgreSQL có thể thử lại. Vi phạm ràng buộc khác không bị bọc: exception gốc lan ra.
    /// </exception>
    Task<InsertExecutionResult> InsertExecutionAsync(DecisionExecutionRecord record, CancellationToken cancellationToken);
}
