using System.Collections;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Application.Options;
using TADSS.Infrastructure.Options;
using TADSS.Runtime.Api.Composition;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-14: <c>appsettings.json</c> (không nạp <c>appsettings.Development.json</c>) qua mọi validator của runtime khi host khởi động,
/// và mỗi options của Application/Infrastructure/Runtime.Api (kiểu public không lồng, tên <c>*Options</c>, có hằng <c>SectionName</c> —
/// tìm bằng reflection, không dùng danh sách cứng) đều có đúng một <c>IValidateOptions&lt;T&gt;</c> được đăng ký cùng <c>ValidateOnStart</c>.
/// Ngoại lệ khai báo duy nhất: <c>DatabaseOptions</c> (chỉ có một cờ <c>bool</c>, không có gì để kiểm). Options của strategy nằm ở project strategy
/// và tự kiểm bằng <c>Validate()</c> khi dựng registry nên không thuộc quy tắc này.
/// </summary>
public class AppSettingsValidationTests
{
    /// <summary>Options có <c>SectionName</c> nhưng được phép không có validator, kèm lý do.</summary>
    private static readonly Dictionary<Type, string> OptionsWithoutValidator = new()
    {
        [typeof(DatabaseOptions)] = "chỉ có một bool, không có gì để kiểm",
    };

    private static IHost BuildHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false);
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        builder.Services.AddPresentation(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Không options nào lỗi khi khởi động và mọi giá trị đọc ra khớp mặc định đã chốt.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public async Task AppSettings_PassAllValidators()
    {
        using var host = BuildHost();

        await host.StartAsync();
        await host.StopAsync();

        var services = host.Services;
        services.GetRequiredService<IOptions<ApprovalOptions>>().Value.WindowMinutes.Should().Be(30);
        services.GetRequiredService<IOptions<ValidationOptions>>().Value.MaxItemMinutes.Should().Be(720);
        services.GetRequiredService<IOptions<CalendarOptions>>().Value.Should().NotBeNull();
        services.GetRequiredService<IOptions<StrategyOptions>>().Value.ActiveStrategyName.Should().Be("DefaultDecisionStrategy");
        services.GetRequiredService<IOptions<ResilienceOptions>>().Value.OuterTimeoutMs.Should().Be(500);
        var cache = services.GetRequiredService<IOptions<CacheOptions>>().Value;
        cache.Provider.Should().Be(CacheProviders.Redis);
        cache.RedisConnectTimeoutMs.Should().Be(3000);
        services.GetRequiredService<IOptions<ErrorHandlingOptions>>().Value.RetryAfterSeconds.Should().Be(5);
    }

    /// <summary>Mỗi options có <c>SectionName</c> (trừ ngoại lệ khai báo) có đúng một validator đăng ký; ngoại lệ thì không có validator nào.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AppSettings_EveryValidatableOptions_HasRegisteredValidator()
    {
        var optionsTypes = DiscoverSectionOptions();
        using var host = BuildHost();

        var violations = optionsTypes
            .Select(type => (Type: type, Count: host.Services.GetServices(typeof(IValidateOptions<>).MakeGenericType(type)).Count()))
            .Where(pair => OptionsWithoutValidator.ContainsKey(pair.Type) ? pair.Count != 0 : pair.Count != 1)
            .Select(pair => $"{pair.Type.FullName}: {pair.Count} validator")
            .ToList();

        violations.Should().BeEmpty("mỗi options có SectionName phải có đúng một IValidateOptions<T> (ngoại lệ khai báo: {0}); vi phạm: {1}",
            string.Join(", ", OptionsWithoutValidator.Select(pair => $"{pair.Key.Name} ({pair.Value})")), string.Join("; ", violations));
    }

    /// <summary>
    /// Mỗi options có validator đều đăng ký <c>ValidateOnStart()</c>. .NET 8 ghi mỗi lần gọi <c>ValidateOnStart</c> vào <c>StartupValidatorOptions</c>
    /// (kiểu internal của Microsoft.Extensions.Options, từ điển khóa <c>(Type, tên options)</c>); test đọc từ điển đó bằng reflection để chứng minh
    /// đúng tập kiểu bị kiểm khi khởi động. Nếu framework đổi cấu trúc nội bộ này, test đỏ kèm lời nhắc cập nhật.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AppSettings_EveryValidatedOptions_IsRegisteredForValidateOnStart()
    {
        var optionsTypes = DiscoverSectionOptions();
        using var host = BuildHost();
        var startupValidated = ReadStartupValidatedKeys(host.Services);

        startupValidated.Should().NotBeEmpty("ValidateOnStart phải đăng ký ít nhất một options, không được pass rỗng");

        var violations = optionsTypes
            .Select(type => (Type: type, Registered: startupValidated.Contains((type, MsOptions.DefaultName))))
            .Where(pair => OptionsWithoutValidator.ContainsKey(pair.Type) ? pair.Registered : !pair.Registered)
            .Select(pair => $"{pair.Type.FullName}: {(pair.Registered ? "có" : "thiếu")} ValidateOnStart")
            .ToList();

        violations.Should().BeEmpty("mỗi options có validator phải gọi ValidateOnStart() (ngoại lệ không validator thì không được gọi); vi phạm: {0}",
            string.Join("; ", violations));
    }

    /// <summary>
    /// Options của tầng lõi: kiểu public không lồng, không abstract, tên kết thúc <c>Options</c> và có hằng <c>string SectionName</c>.
    /// Kiểu phụ như <c>PeakWindowOptions</c> (phần tử của mảng cấu hình) không có <c>SectionName</c> nên không thuộc tập này.
    /// </summary>
    private static IReadOnlyList<Type> DiscoverSectionOptions()
    {
        var types = SourceTree.RuntimeProjects
            .SelectMany(project => SourceTree.AssemblyOf(project).GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true, IsNested: false }
                && type.Name.EndsWith("Options", StringComparison.Ordinal)
                && type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) is { IsLiteral: true } field
                && field.FieldType == typeof(string))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        types.Should().NotBeEmpty("phải tìm thấy options có SectionName trong Application/Infrastructure/Runtime.Api, không được pass rỗng");
        types.Should().Contain(OptionsWithoutValidator.Keys, "ngoại lệ khai báo phải còn là options có SectionName (xóa khỏi danh sách ngoại lệ nếu kiểu đã đổi)");
        return types;
    }

    /// <summary>Đọc các khóa <c>(Type, tên options)</c> mà <c>ValidateOnStart</c> đã đăng ký trong <c>StartupValidatorOptions</c> (internal) của framework.</summary>
    private static HashSet<(Type Type, string Name)> ReadStartupValidatedKeys(IServiceProvider services)
    {
        const string frameworkNote = "cấu trúc nội bộ của Microsoft.Extensions.Options đã đổi: cập nhật cách đọc StartupValidatorOptions._validators.";
        var holderType = typeof(IValidateOptions<>).Assembly.GetType("Microsoft.Extensions.Options.StartupValidatorOptions");
        if (holderType is null)
        {
            throw new InvalidOperationException($"Không tìm thấy StartupValidatorOptions: {frameworkNote}");
        }

        var holder = services.GetRequiredService(typeof(IOptions<>).MakeGenericType(holderType));
        var value = holder.GetType().GetProperty("Value")?.GetValue(holder);
        var validators = value?.GetType()
            .GetProperty("_validators", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetValue(value) as IDictionary;
        if (validators is null)
        {
            throw new InvalidOperationException($"Không đọc được StartupValidatorOptions._validators: {frameworkNote}");
        }

        return validators.Keys.Cast<(Type Type, string Name)>().ToHashSet();
    }
}
