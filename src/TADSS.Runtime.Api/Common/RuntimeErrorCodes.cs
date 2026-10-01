namespace TADSS.Runtime.Api.Common;

/// <summary>
/// Mã lỗi của runtime chưa có trong contract SDK v1 (đóng băng). Chuyển vào <c>ErrorCodes</c> của SDK v2 ở gói 5.2.
/// </summary>
public static class RuntimeErrorCodes
{
    /// <summary>Không đọc/ghi được audit do PostgreSQL tạm thời không khả dụng (fail-closed).</summary>
    public const string PersistenceUnavailable = "PERSISTENCE_UNAVAILABLE";
}
