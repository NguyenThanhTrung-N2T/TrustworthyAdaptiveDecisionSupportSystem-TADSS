namespace TADSS.Infrastructure.Options;

/// <summary>
/// Cấu hình telemetry (section <c>Tadss:Telemetry</c>, O-03): gửi trace, metric và log của runtime tới collector OpenTelemetry
/// (<c>grafana/otel-lgtm</c>). Mặc định <b>tắt</b>: khi <see cref="Enabled"/> là <c>false</c> không có provider OpenTelemetry nào được đăng ký
/// và worker làm mới gauge hàng chờ không truy vấn; meter và ActivitySource của runtime vẫn tồn tại nên test đo được mà không cần exporter.
/// Collector không tới được thì request không bị ảnh hưởng (exporter chạy nền).
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>Tên section cấu hình.</summary>
    public const string SectionName = "Tadss:Telemetry";

    /// <summary>Độ dài tối đa của <see cref="ServiceName"/>.</summary>
    public const int MaxServiceNameLength = 64;

    /// <summary>Bật telemetry (mặc định <c>false</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>Địa chỉ collector (URI tuyệt đối http/https; bắt buộc hợp lệ khi <see cref="Enabled"/>).</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>Giao thức OTLP.</summary>
    public TelemetryOtlpProtocol OtlpProtocol { get; set; } = TelemetryOtlpProtocol.Grpc;

    /// <summary>Giá trị thuộc tính resource <c>service.name</c>.</summary>
    public string ServiceName { get; set; } = "tadss-runtime";

    /// <summary>Giá trị <c>service.instance.id</c>; rỗng thì dùng tên máy/container.</summary>
    public string ServiceInstanceId { get; set; } = "";

    /// <summary>Tỉ lệ lấy mẫu trace, trong [0, 1] (metric và audit không lấy mẫu).</summary>
    public double TraceSampleRatio { get; set; } = 1.0;

    /// <summary>Gửi log tới collector (ngoài console) khi <see cref="Enabled"/>.</summary>
    public bool ExportLogs { get; set; } = true;

    /// <summary>Chu kỳ xuất metric (ms), 1000..60000.</summary>
    public int MetricExportIntervalMs { get; set; } = 10_000;

    /// <summary>Chu kỳ làm mới gauge số quyết định chờ duyệt (giây), 5..3600.</summary>
    public int PendingApprovalsRefreshSeconds { get; set; } = 30;
}
