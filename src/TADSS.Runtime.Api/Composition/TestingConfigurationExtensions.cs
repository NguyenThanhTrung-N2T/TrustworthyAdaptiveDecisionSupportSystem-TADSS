namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Nguồn cấu hình dành riêng cho môi trường <c>Testing</c> (O-17): file JSON do evaluator ghi để đổi nóng profile tiêm lỗi giữa một lượt tải (xem <c>FaultInjectionControl</c>).
/// Môi trường khác không bao giờ đọc biến hay file này.
/// </summary>
public static class TestingConfigurationExtensions
{
    /// <summary>Tên biến môi trường chứa đường dẫn tuyệt đối của file JSON tiêm lỗi.</summary>
    public const string FaultInjectionFileVariable = "TADSS_FAULT_INJECTION_FILE";

    /// <summary>
    /// Chỉ khi <paramref name="environment"/> là <see cref="StrategyServiceCollectionExtensions.FaultInjectionEnvironment"/> <b>và</b> biến <see cref="FaultInjectionFileVariable"/> có giá trị:
    /// thêm file đó (tùy chọn, <c>reloadOnChange</c>) vào <b>cuối</b> chuỗi nguồn nên ưu tiên hơn biến môi trường. Ngược lại không làm gì và không đọc file.
    /// File được ghi nguyên tử (ghi file tạm rồi đổi tên) nên thường không đọc nửa chừng; nếu vẫn gặp nội dung cụt, lỗi đọc bị bỏ qua và <c>FaultInjectionControl</c> giữ bản cũ.
    /// Thư mục chứa file chưa tồn tại thì bỏ qua (nguồn tùy chọn).
    /// </summary>
    public static IConfigurationBuilder AddTadssTestingConfiguration(this IConfigurationBuilder configuration, IHostEnvironment environment)
    {
        return configuration.AddFaultInjectionFile(environment, Environment.GetEnvironmentVariable(FaultInjectionFileVariable));
    }

    /// <summary>Phần cốt lõi của <see cref="AddTadssTestingConfiguration"/> với đường dẫn truyền thẳng (không đọc biến môi trường) để test không phải đặt biến toàn tiến trình.</summary>
    internal static IConfigurationBuilder AddFaultInjectionFile(this IConfigurationBuilder configuration, IHostEnvironment environment, string? path)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsEnvironment(StrategyServiceCollectionExtensions.FaultInjectionEnvironment)
            || string.IsNullOrWhiteSpace(path)
            || !Path.IsPathRooted(path)
            || !Directory.Exists(Path.GetDirectoryName(path)))
        {
            return configuration;
        }

        // OnLoadException bỏ qua lỗi đọc: nếu không, file cụt/đang ghi dở làm callback nạp lại ném trên luồng theo dõi file và kéo sập tiến trình. Dữ liệu của nguồn bị xóa
        // khi nạp lại lỗi nên FaultInjectionControl thấy section biến mất, giữ cấu hình cũ và đếm một lần nạp lại thất bại.
        return configuration.AddJsonFile(source =>
        {
            source.Path = path;
            source.Optional = true;
            source.ReloadOnChange = true;
            source.OnLoadException = context => context.Ignore = true;
            source.ResolveFileProvider();
        });
    }
}
