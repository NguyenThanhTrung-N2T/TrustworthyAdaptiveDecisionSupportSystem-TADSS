namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Wrapper response chuẩn cho API theo phong cách SmashCourt — client luôn nhận cùng một format.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }

    /// <summary>
    /// Machine-readable code — Client dùng để xử lý logic theo loại lỗi.
    /// Ví dụ: "SUCCESS", "VALIDATION_ERROR", "NOT_FOUND", "IDEMPOTENCY_KEY_REUSE"
    /// </summary>
    public string Code { get; set; } = ErrorCodes.Success;

    public string? Message { get; set; }

    public T? Data { get; set; }

    /// <summary>
    /// Chỉ có giá trị khi có lỗi validation — chứa danh sách lỗi theo field.
    /// </summary>
    public Dictionary<string, string[]>? Errors { get; set; }

    // ─── Factory helpers ─────────────────────────────────────────────────

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Code = ErrorCodes.Success,
        Message = message,
        Data = data
    };

    public static ApiResponse<T> Fail(
        string message,
        string code = ErrorCodes.InternalError,
        Dictionary<string, string[]>? errors = null) => new()
    {
        Success = false,
        Code = code,
        Message = message,
        Errors = errors
    };
}

/// <summary>
/// Non-generic version — dùng khi response không có data hoặc từ error handler.
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    public static new ApiResponse<object> Ok(object? data = null, string? message = null) => new()
    {
        Success = true,
        Code = ErrorCodes.Success,
        Message = message,
        Data = data
    };

    public static new ApiResponse<object> Fail(
        string message,
        string code = ErrorCodes.InternalError,
        Dictionary<string, string[]>? errors = null) => new()
    {
        Success = false,
        Code = code,
        Message = message,
        Errors = errors
    };
}
