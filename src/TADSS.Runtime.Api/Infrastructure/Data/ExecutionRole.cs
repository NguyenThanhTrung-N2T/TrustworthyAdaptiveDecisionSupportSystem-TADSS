namespace TADSS.Runtime.Api.Infrastructure.Data;

/// <summary>
/// Vai trò của một bản ghi đánh giá (cột <c>role</c> của <c>tadss.decision_executions</c>).
/// Tên thành viên là giá trị lưu trong cột (<c>CHECK (role IN ('PRIMARY', 'SHADOW'))</c>).
/// </summary>
public enum ExecutionRole
{
    /// <summary>Quyết định chính, là kết quả trả cho client; chịu ràng buộc idempotency.</summary>
    PRIMARY = 0,

    /// <summary>Đánh giá bóng (strategy thử nghiệm chạy song song), không ảnh hưởng kết quả trả cho client.</summary>
    SHADOW = 1
}
