using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TADSS.Application.Common;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// OB-5 / UT-14: <c>AddTadssTelemetry</c> chỉ đăng ký OpenTelemetry khi <c>Tadss:Telemetry:Enabled = true</c>; khi bật, resource có bốn thuộc tính O-04,
/// provider nghe nguồn và meter của TADSS, và cấu hình sai làm host không khởi động (cùng thông báo của validator).
/// </summary>
public class TelemetryRegistrationTests : IClassFixture<DecisionsApiFactory>
{
    private const string UnreachableEndpoint = "http://127.0.0.1:1";

    private readonly DecisionsApiFactory _factory;

    public TelemetryRegistrationTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    /// <summary>
    /// Host với cấu hình <c>Tadss:Telemetry:*</c>. <c>AddTadssTelemetry</c> đọc cấu hình ngay lúc đăng ký (quyết định có đăng ký provider hay không) trong khi nguồn từ
    /// <c>ConfigureAppConfiguration</c> chỉ được nạp lúc <c>Build()</c>, nên cấu hình đi qua <c>UseSetting</c> (nạp vào cấu hình host trước khi <c>Program</c> chạy).
    /// </summary>
    private WebApplicationFactory<Program> CreateHost(params (string Key, string Value)[] settings) =>
        _factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting($"Tadss:Telemetry:{key}", value);
            }
        });

    /// <summary>Mặc định (<c>Enabled = false</c>): DI không có <c>TracerProvider</c> lẫn <c>MeterProvider</c>.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void AddTadssTelemetry_Disabled_RegistersNoProviders()
    {
        using var factory = CreateHost(("Enabled", "false"));
        using var client = factory.CreateClient();

        factory.Services.GetService<TracerProvider>().Should().BeNull();
        factory.Services.GetService<MeterProvider>().Should().BeNull();
    }

    /// <summary>
    /// Bật telemetry: resource có <c>service.name</c>, <c>service.version</c>, <c>service.instance.id</c> (đúng giá trị cấu hình) và
    /// <c>deployment.environment.name</c>; nguồn trace <c>TADSS.Runtime</c> có listener; <c>MeterProvider</c> được đăng ký.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void AddTadssTelemetry_Enabled_ResourceAndSources()
    {
        using var factory = CreateHost(
            ("Enabled", "true"), ("OtlpEndpoint", UnreachableEndpoint), ("ServiceInstanceId", "tadss-x"), ("ExportLogs", "false"));
        using var client = factory.CreateClient();

        var tracerProvider = factory.Services.GetRequiredService<TracerProvider>();
        var attributes = tracerProvider.GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
        attributes["service.name"].Should().Be("tadss-runtime");
        attributes["service.instance.id"].Should().Be("tadss-x");
        attributes["deployment.environment.name"].Should().Be(DecisionsApiFactory.EnvironmentName);
        attributes["service.version"].Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
        TadssActivitySource.Source.HasListeners().Should().BeTrue("provider phải nghe nguồn TADSS.Runtime");
        factory.Services.GetRequiredService<MeterProvider>().Should().NotBeNull();
    }

    /// <summary><c>ServiceInstanceId</c> rỗng → <c>service.instance.id</c> là tên máy/container.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void AddTadssTelemetry_EmptyInstanceId_UsesMachineName()
    {
        using var factory = CreateHost(
            ("Enabled", "true"), ("OtlpEndpoint", UnreachableEndpoint), ("ExportLogs", "false"));
        using var client = factory.CreateClient();

        var attributes = factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);

        attributes["service.instance.id"].Should().Be(Environment.MachineName);
    }

    /// <summary>Cấu hình sai (tỉ lệ lấy mẫu ngoài [0,1]; endpoint không hợp lệ khi bật) → host không khởi động, thông báo nêu đúng khóa sai.</summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData("TraceSampleRatio", "2", "TraceSampleRatio")]
    [InlineData("OtlpEndpoint", "không-phải-uri", "OtlpEndpoint")]
    public void AddTadssTelemetry_InvalidConfiguration_FailsFast(string key, string value, string expectedFragment)
    {
        using var factory = CreateHost(("Enabled", "true"), (key, value));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(expectedFragment);
    }
}
