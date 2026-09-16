namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Ngoại lệ đại diện cho lỗi vi phạm cấu trúc hoặc giá trị không hợp lệ của Hợp đồng dữ liệu (Contract DTO layer).
/// </summary>
public class ContractValidationException : Exception
{
    /// <summary>
    /// Mã lỗi vi phạm hợp đồng (ví dụ: INVALID_BOOKING_PRIORITY, INVALID_PAYMENT_STATUS).
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Tên trường hoặc thuộc tính trên hợp đồng DTO gây ra lỗi (tùy chọn).
    /// </summary>
    public string? FieldName { get; }

    /// <summary>
    /// Khởi tạo một đối tượng ContractValidationException với mã lỗi và thông điệp chi tiết.
    /// </summary>
    /// <param name="errorCode">Mã lỗi hợp đồng.</param>
    /// <param name="message">Thông điệp chi tiết giải thích vi phạm hợp đồng.</param>
    /// <param name="fieldName">Tên thuộc tính vi phạm (tùy chọn).</param>
    public ContractValidationException(string errorCode, string message, string? fieldName = null)
        : base(message)
    {
        ErrorCode = errorCode;
        FieldName = fieldName;
    }
}
