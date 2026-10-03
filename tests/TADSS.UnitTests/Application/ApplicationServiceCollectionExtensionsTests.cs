using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Application;
using TADSS.Application.Decisions;
using TADSS.Application.Strategies;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Application;

/// <summary>
/// <c>AddApplication</c> là nơi duy nhất đăng ký DI cho tầng Application: vòng đời của use case/policy và
/// kiểm tra cấu hình fail-fast khi khởi động.
/// </summary>
public class ApplicationServiceCollectionExtensionsTests
{
    public static TheoryData<Type, ServiceLifetime> ExpectedLifetimes => new()
    {
        { typeof(IDecisionService), ServiceLifetime.Scoped },
        { typeof(IDecisionQueryService), ServiceLifetime.Scoped },
        { typeof(IStrategyRegistry), ServiceLifetime.Singleton },
        { typeof(DecisionResponseComposer), ServiceLifetime.Singleton },
        { typeof(ApprovalWindowPolicy), ServiceLifetime.Singleton },
        { typeof(DepositCalculator), ServiceLifetime.Singleton },
        { typeof(ItemDurationPolicy), ServiceLifetime.Singleton },
    };

    public static TheoryData<string, string[]> InvalidOptions => new()
    {
        { "ApprovalWindowZero", ["Tadss:Approval:WindowMinutes", "0"] },
        { "MinItemGreaterThanMax", ["Tadss:Validation:MinItemMinutes", "90", "Tadss:Validation:MaxItemMinutes", "60"] },
        {
            "PeakWindowStartAfterEnd",
            ["Tadss:Calendar:PeakWindows:0:Day", "Monday", "Tadss:Calendar:PeakWindows:0:Start", "21:00", "Tadss:Calendar:PeakWindows:0:End", "17:00"]
        },
    };

    /// <summary>Use case đăng ký đúng vòng đời: service scoped (vì dùng kho audit scoped), registry/policy/composer singleton.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(ExpectedLifetimes))]
    public void AddApplication_RegistersUseCasesWithExpectedLifetimes_Theory(Type serviceType, ServiceLifetime expected)
    {
        var services = new ServiceCollection();

        services.AddApplication(LoadAppSettings());

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == serviceType).Subject;
        descriptor.Lifetime.Should().Be(expected);
    }

    /// <summary>Các singleton không cần strategy từ composition root dựng được từ cấu hình mẫu trong appsettings.json.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AddApplication_AppSettingsConfiguration_ResolvesPoliciesAndComposer()
    {
        var services = new ServiceCollection();
        services.AddApplication(LoadAppSettings());
        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<ItemDurationPolicy>().Should().BeSameAs(provider.GetRequiredService<ItemDurationPolicy>());
        provider.GetRequiredService<DepositCalculator>().Should().NotBeNull();
        provider.GetRequiredService<ApprovalWindowPolicy>().Should().NotBeNull();
        provider.GetRequiredService<DecisionResponseComposer>().Should().NotBeNull();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    /// <summary>Cấu hình sai (approval window = 0, min &gt; max, lịch sai) làm host không khởi động được.</summary>
    [Theory]
    [Trait("CatalogId", "UT-01")]
    [MemberData(nameof(InvalidOptions))]
    public async Task AddApplication_InvalidOptions_FailsOnStart_Theory(string scenario, string[] keyValues)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(ToPairs(keyValues));
        builder.Services.AddApplication(builder.Configuration);
        using var host = builder.Build();

        var act = () => host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>($"kịch bản {scenario} phải bị chặn khi khởi động");
    }

    private static IConfiguration LoadAppSettings() =>
        new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();

    private static IEnumerable<KeyValuePair<string, string?>> ToPairs(string[] keyValues)
    {
        for (var index = 0; index < keyValues.Length; index += 2)
        {
            yield return new KeyValuePair<string, string?>(keyValues[index], keyValues[index + 1]);
        }
    }
}
