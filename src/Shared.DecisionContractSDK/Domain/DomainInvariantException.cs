namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Ngoại lệ đại diện cho việc vi phạm một quy tắc bất biến nghiệp vụ cốt lõi (Domain Invariant) trong mô hình TADSS.
/// </summary>
public class DomainInvariantException : Exception
{
    /// <summary>
    /// Mã lỗi bất biến chuẩn hóa machine-readable (ví dụ: INVALID_SCHEDULE_RANGE, DEPOSIT_EXCEEDS_TOTAL_PRICE).
    /// </summary>
    public string InvariantCode { get; }

    /// <summary>
    /// Tên trường hoặc thuộc tính gây ra vi phạm Invariant (tùy chọn).
    /// </summary>
    public string? FieldName { get; }

    /// <summary>
    /// Khởi tạo ngoại lệ DomainInvariantException.
    /// </summary>
    /// <param name="invariantCode">Mã lỗi bất biến.</param>
    /// <param name="message">Thông điệp chi tiết mô tả lỗi.</param>
    /// <param name="fieldName">Tên thuộc tính vi phạm (tùy chọn).</param>
    public DomainInvariantException(string invariantCode, string message, string? fieldName = null)
        : base(message)
    {
        InvariantCode = invariantCode;
        FieldName = fieldName;
    }
}
