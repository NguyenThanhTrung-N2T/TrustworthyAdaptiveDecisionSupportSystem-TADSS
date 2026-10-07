using FluentAssertions;
using TADSS.Infrastructure.Options;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Options;

/// <summary>
/// OB-5: <see cref="TelemetryOptionsValidator"/> chặn cấu hình telemetry sai ngay lúc khởi động (ở mọi môi trường, kể cả khi <c>Enabled = false</c>
/// vì options luôn đăng ký): endpoint là URI tuyệt đối http/https khi bật, tỉ lệ lấy mẫu trong [0, 1], chu kỳ xuất metric 1000..60000 ms,
/// chu kỳ làm mới gauge 5..3600 giây, <c>ServiceName</c> không rỗng và tối đa 64 ký tự.
/// </summary>
public class TelemetryOptionsValidatorTests
{
    private static readonly TelemetryOptionsValidator Validator = new();

    /// <summary>Mỗi ca sai bị từ chối và message nêu đúng khóa; các ca biên hợp lệ đạt.</summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData(true, "/tương-đối", 1.0, 10_000, 30, "tadss-runtime", false, "OtlpEndpoint")]
    [InlineData(true, "ftp://collector:4317", 1.0, 10_000, 30, "tadss-runtime", false, "OtlpEndpoint")]
    [InlineData(true, "", 1.0, 10_000, 30, "tadss-runtime", false, "OtlpEndpoint")]
    [InlineData(false, "khong-hop-le", 1.0, 10_000, 30, "tadss-runtime", true, null)]
    [InlineData(true, "http://localhost:4317", -0.1, 10_000, 30, "tadss-runtime", false, "TraceSampleRatio")]
    [InlineData(true, "http://localhost:4317", 1.1, 10_000, 30, "tadss-runtime", false, "TraceSampleRatio")]
    [InlineData(true, "http://localhost:4317", 0.0, 10_000, 30, "tadss-runtime", true, null)]
    [InlineData(true, "http://localhost:4317", 1.0, 999, 30, "tadss-runtime", false, "MetricExportIntervalMs")]
    [InlineData(true, "http://localhost:4317", 1.0, 60_001, 30, "tadss-runtime", false, "MetricExportIntervalMs")]
    [InlineData(true, "http://localhost:4317", 1.0, 1_000, 30, "tadss-runtime", true, null)]
    [InlineData(true, "http://localhost:4317", 1.0, 60_000, 30, "tadss-runtime", true, null)]
    [InlineData(true, "http://localhost:4317", 1.0, 10_000, 4, "tadss-runtime", false, "PendingApprovalsRefreshSeconds")]
    [InlineData(true, "http://localhost:4317", 1.0, 10_000, 3_601, "tadss-runtime", false, "PendingApprovalsRefreshSeconds")]
    [InlineData(true, "https://collector:4318", 1.0, 10_000, 5, "tadss-runtime", true, null)]
    [InlineData(true, "http://localhost:4317", 1.0, 10_000, 3_600, "tadss-runtime", true, null)]
    [InlineData(true, "http://localhost:4317", 1.0, 10_000, 30, "", false, "ServiceName")]
    [InlineData(true, "http://localhost:4317", 1.0, 10_000, 30, "   ", false, "ServiceName")]
    public void Validate_Theory(
        bool enabled,
        string endpoint,
        double ratio,
        int exportIntervalMs,
        int refreshSeconds,
        string serviceName,
        bool expectedValid,
        string? expectedKey)
    {
        var options = new TelemetryOptions
        {
            Enabled = enabled,
            OtlpEndpoint = endpoint,
            TraceSampleRatio = ratio,
            MetricExportIntervalMs = exportIntervalMs,
            PendingApprovalsRefreshSeconds = refreshSeconds,
            ServiceName = serviceName,
        };

        var result = Validator.Validate(null, options);

        result.Succeeded.Should().Be(expectedValid);
        if (expectedKey is not null)
        {
            result.FailureMessage.Should().Contain($"{TelemetryOptions.SectionName}:{expectedKey}");
        }
    }

    /// <summary><c>ServiceName</c> dài đúng 64 ký tự đạt, 65 ký tự bị từ chối.</summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData(TelemetryOptions.MaxServiceNameLength, true)]
    [InlineData(TelemetryOptions.MaxServiceNameLength + 1, false)]
    public void Validate_ServiceNameLength_Boundary(int length, bool expectedValid)
    {
        var options = new TelemetryOptions { ServiceName = new string('a', length) };

        Validator.Validate(null, options).Succeeded.Should().Be(expectedValid);
    }

    /// <summary>Mặc định (tắt, gRPC, tỉ lệ 1.0, xuất 10 s, làm mới 30 s) hợp lệ và <c>Enabled = false</c>.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void Validate_Defaults_AreValidAndDisabled()
    {
        var options = new TelemetryOptions();

        Validator.Validate(null, options).Succeeded.Should().BeTrue();
        options.Enabled.Should().BeFalse();
        options.OtlpProtocol.Should().Be(TelemetryOtlpProtocol.Grpc);
        options.TraceSampleRatio.Should().Be(1.0);
        options.ExportLogs.Should().BeTrue();
        options.MetricExportIntervalMs.Should().Be(10_000);
        options.PendingApprovalsRefreshSeconds.Should().Be(30);
        options.ServiceInstanceId.Should().BeEmpty();
    }

    /// <summary>Nhiều lỗi cùng lúc được báo đủ trong một kết quả (không dừng ở lỗi đầu).</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void Validate_MultipleFailures_AllReported()
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = "x", TraceSampleRatio = 2, ServiceName = "" };

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }
}
