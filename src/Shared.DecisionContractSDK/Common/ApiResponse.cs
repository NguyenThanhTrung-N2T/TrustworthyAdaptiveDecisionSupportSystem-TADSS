namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Wrapper response chuẩn cho API theo phong cách SmashCourt — client luôn nhận cùng một format.
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu của payload trả về.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Cờ trạng thái thực thi thành công (true) hay thất bại (false).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Machine-readable code — Client dùng để xử lý logic theo loại lỗi.
    /// Ví dụ: "SUCCESS", "VALIDATION_ERROR", "NOT_FOUND", "IDEMPOTENCY_KEY_REUSE"
    /// </summary>
    public string Code { get; set; } = ErrorCodes.Success;

    /// <summary>
    /// Thông điệp mô tả kết quả hoặc giải thích lỗi ngắn gọn cho người dùng.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Dữ liệu trả về của API.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Chỉ có giá trị khi có lỗi validation — chứa danh sách lỗi theo field.
    /// </summary>
    public Dictionary<string, string[]>? Errors { get; set; }

    // ─── Factory helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Khởi tạo response thành công kèm dữ liệu data.
    /// </summary>
    /// <param name="data">Dữ liệu trả về.</param>
    /// <param name="message">Thông điệp tùy chọn.</param>
    /// <returns>Đối tượng ApiResponse mang trạng thái thành công.</returns>
    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Code = ErrorCodes.Success,
        Message = message,
        Data = data
    };

    /// <summary>
    /// Khởi tạo response thất bại với mã lỗi và thông điệp.
    /// </summary>
    /// <param name="message">Thông điệp mô tả lỗi.</param>
    /// <param name="code">Mã lỗi máy đọc (mặc định INTERNAL_ERROR).</param>
    /// <param name="errors">Danh sách chi tiết lỗi theo field (tùy chọn).</param>
    /// <returns>Đối tượng ApiResponse mang trạng thái thất bại.</returns>
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
    /// <summary>
    /// Khởi tạo response thành công không định kiểu.
    /// </summary>
    /// <param name="data">Dữ liệu tùy chọn.</param>
    /// <param name="message">Thông điệp tùy chọn.</param>
    /// <returns>Đối tượng ApiResponse thành công.</returns>
    public static new ApiResponse<object> Ok(object? data = null, string? message = null) => new()
    {
        Success = true,
        Code = ErrorCodes.Success,
        Message = message,
        Data = data
    };

    /// <summary>
    /// Khởi tạo response thất bại không định kiểu.
    /// </summary>
    /// <param name="message">Thông điệp lỗi.</param>
    /// <param name="code">Mã lỗi (mặc định INTERNAL_ERROR).</param>
    /// <param name="errors">Danh sách chi tiết lỗi theo field (tùy chọn).</param>
    /// <returns>Đối tượng ApiResponse thất bại.</returns>
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
