namespace TADSS.UnitTests.TestData;

/// <summary>
/// Chuỗi kết nối giả cho test dựng DI qua <c>AddDecisionRuntime</c>/<c>AddInfrastructure</c>: đăng ký DbContext và health check
/// PostgreSQL cần chuỗi kết nối nhưng không bao giờ mở kết nối (cổng 1 không có dịch vụ nào lắng nghe).
/// </summary>
internal static class TestConnectionStrings
{
    /// <summary>Khóa cấu hình của chuỗi kết nối mặc định.</summary>
    public const string Key = "ConnectionStrings:DefaultConnection";

    /// <summary>Chuỗi kết nối giả không trỏ tới database thật.</summary>
    public const string Value = "Host=127.0.0.1;Port=1;Database=none";

    /// <summary>Cặp khóa/giá trị để thêm vào <c>AddInMemoryCollection</c>.</summary>
    public static KeyValuePair<string, string?> Pair { get; } = new(Key, Value);
}
