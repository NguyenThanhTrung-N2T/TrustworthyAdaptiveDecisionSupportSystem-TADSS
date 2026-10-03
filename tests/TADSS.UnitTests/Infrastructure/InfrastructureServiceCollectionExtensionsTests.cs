using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Infrastructure;
using TADSS.Infrastructure.Persistence;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Infrastructure;

/// <summary>
/// <c>AddInfrastructure</c> là nơi duy nhất đăng ký DI cho tầng Infrastructure: vòng đời của adapter và
/// việc không mở kết nối Redis/PostgreSQL chỉ vì resolve service.
/// </summary>
public class InfrastructureServiceCollectionExtensionsTests
{
    public static TheoryData<Type, ServiceLifetime> ExpectedLifetimes => new()
    {
        { typeof(EfDecisionAuditStore), ServiceLifetime.Scoped },
        { typeof(IDecisionEvaluationStore), ServiceLifetime.Scoped },
        { typeof(IDecisionQueryStore), ServiceLifetime.Scoped },
        { typeof(IDecisionGovernanceStore), ServiceLifetime.Scoped },
        { typeof(TadssDbContext), ServiceLifetime.Scoped },
        { typeof(IStrategyPipeline), ServiceLifetime.Singleton },
        { typeof(IStrategyDecisionCache), ServiceLifetime.Singleton },
    };

    /// <summary>Adapter đăng ký đúng vòng đời: kho audit và DbContext scoped, pipeline và cache singleton.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(ExpectedLifetimes))]
    public void AddInfrastructure_RegistersAdaptersWithExpectedLifetimes_Theory(Type serviceType, ServiceLifetime expected)
    {
        var services = new ServiceCollection();

        var configuration = BuildConfiguration();
        // Như host thật: IConfiguration có trong DI (DatabaseOptions đọc chuỗi kết nối từ đó)
        services.AddSingleton(configuration);
        services.AddInfrastructure(configuration);

        var descriptor = services.Should().ContainSingle(d => d.ServiceType == serviceType).Subject;
        descriptor.Lifetime.Should().Be(expected);
    }

    /// <summary>Với <c>Provider = Memory</c>, resolve adapter không tạo kết nối Redis (factory multiplexer không bao giờ chạy) và không mở kết nối PostgreSQL.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AddInfrastructure_MemoryProvider_ResolvesAdaptersWithoutConnecting()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // Đăng ký có sẵn thắng TryAdd của AddInfrastructure; nếu bị resolve thì test thất bại
        services.AddSingleton<IConnectionMultiplexer>(_ => throw new InvalidOperationException("Không được kết nối Redis khi Provider = Memory."));
        var configuration = BuildConfiguration();
        // Như host thật: IConfiguration có trong DI (DatabaseOptions đọc chuỗi kết nối từ đó)
        services.AddSingleton(configuration);
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IStrategyPipeline>().Should().BeSameAs(provider.GetRequiredService<IStrategyPipeline>());
        provider.GetRequiredService<IStrategyDecisionCache>().Should().BeSameAs(provider.GetRequiredService<IStrategyDecisionCache>());
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>().Should().BeOfType<EfDecisionAuditStore>();
        scope.ServiceProvider.GetRequiredService<IDecisionQueryStore>().Should().BeOfType<EfDecisionAuditStore>();
        scope.ServiceProvider.GetRequiredService<IDecisionGovernanceStore>().Should().BeOfType<EfDecisionGovernanceStore>();
        scope.ServiceProvider.GetRequiredService<TadssDbContext>().Should().NotBeNull();
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tadss:Cache:Provider"] = "Memory",
                [TestConnectionStrings.Key] = TestConnectionStrings.Value,
            })
            .Build();
}
