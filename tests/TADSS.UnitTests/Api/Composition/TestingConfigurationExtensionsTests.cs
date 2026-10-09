using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TADSS.Runtime.Api.Composition;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-09 (O-17): <c>AddTadssTestingConfiguration</c> chỉ thêm file JSON tiêm lỗi (<c>TADSS_FAULT_INJECTION_FILE</c>) khi môi trường là <c>Testing</c> và có đường dẫn tuyệt đối hợp lệ;
/// ngoài ra không thêm nguồn nào và không đọc file. Nguồn thêm vào là tùy chọn, <c>reloadOnChange</c> và nằm <b>cuối</b> (ưu tiên hơn nguồn có trước).
/// Test gọi phần cốt lõi với đường dẫn truyền thẳng để không phải đặt biến môi trường toàn tiến trình.
/// </summary>
public sealed class TestingConfigurationExtensionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tadss-fault-" + Guid.NewGuid().ToString("N"));

    public TestingConfigurationExtensionsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FilePath => Path.Combine(_directory, "fault.json");

    /// <summary>Môi trường khác <c>Testing</c> kèm đường dẫn hợp lệ: không thêm nguồn nào (và file không bị đọc — file không tồn tại vẫn không gây lỗi).</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void AddTadssTestingConfiguration_NonTesting_IgnoresVariable_Theory(string environmentName)
    {
        var builder = new ConfigurationBuilder();

        builder.AddFaultInjectionFile(new FakeEnvironment(environmentName), FilePath);

        builder.Sources.Should().BeEmpty();
    }

    /// <summary>Thiếu giá trị, đường dẫn tương đối hoặc thư mục không tồn tại: không thêm nguồn (nguồn tùy chọn, không làm hỏng khởi động).</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fault.json")]
    public void AddFaultInjectionFile_MissingOrRelativePath_AddsNothing_Theory(string? path)
    {
        var builder = new ConfigurationBuilder();

        builder.AddFaultInjectionFile(new FakeEnvironment("Testing"), path);

        builder.Sources.Should().BeEmpty();
    }

    /// <summary>Thư mục chứa file chưa tồn tại: bỏ qua thay vì ném.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void AddFaultInjectionFile_MissingDirectory_AddsNothing()
    {
        var builder = new ConfigurationBuilder();

        builder.AddFaultInjectionFile(new FakeEnvironment("Testing"), Path.Combine(_directory, "missing", "fault.json"));

        builder.Sources.Should().BeEmpty();
    }

    /// <summary>
    /// <c>Testing</c> + đường dẫn tuyệt đối hợp lệ: thêm đúng một nguồn JSON tùy chọn, <c>reloadOnChange</c>, <b>sau cùng</b>; giá trị trong file ưu tiên hơn nguồn có trước
    /// và đổi file (ghi nguyên tử bằng file tạm rồi đổi tên) được thấy khi nạp lại.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void AddFaultInjectionFile_Testing_AddsOptionalReloadingSourceLast()
    {
        File.WriteAllText(FilePath, """{ "Tadss": { "Testing": { "FaultInjection": { "Profile": "None" } } } }""");
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Tadss:Testing:FaultInjection:Profile", "Delay")]);

        builder.AddFaultInjectionFile(new FakeEnvironment("Testing"), FilePath);
        var configuration = builder.Build();

        var source = builder.Sources.Last().Should().BeOfType<JsonConfigurationSource>().Subject;
        source.Optional.Should().BeTrue();
        source.ReloadOnChange.Should().BeTrue();
        source.Path.Should().Be("fault.json", "đường dẫn tuyệt đối được tách thành thư mục (file provider) + tên file");
        ((PhysicalFileProvider)source.FileProvider!).Root.TrimEnd(Path.DirectorySeparatorChar).Should().Be(_directory);
        var context = new FileLoadExceptionContext { Exception = new InvalidDataException("cụt") };
        source.OnLoadException.Should().NotBeNull("lỗi đọc file không được làm sập tiến trình");
        source.OnLoadException!(context);
        context.Ignore.Should().BeTrue();
        configuration["Tadss:Testing:FaultInjection:Profile"].Should().Be("None", "nguồn thêm sau cùng ưu tiên hơn");

        // Ghi nguyên tử bằng file tạm rồi đổi tên, sau đó nạp lại tường minh: kiểm tra đường dẫn/định dạng được đọc đúng và nguồn này thắng nguồn trước. Việc hệ điều hành báo "file đổi" để
        // nạp lại tự động phụ thuộc inotify (trong container có lúc không nổ trong cả phút khi chạy cả bộ test) nên được kiểm ở integration test FaultInjectionReloadTests trên host thật.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, """{ "Tadss": { "Testing": { "FaultInjection": { "Profile": "Throw" } } } }""");
        File.Move(temp, FilePath, overwrite: true);
        configuration.Reload();
        configuration["Tadss:Testing:FaultInjection:Profile"].Should().Be("Throw");
    }

    private sealed class FakeEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "TADSS.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
