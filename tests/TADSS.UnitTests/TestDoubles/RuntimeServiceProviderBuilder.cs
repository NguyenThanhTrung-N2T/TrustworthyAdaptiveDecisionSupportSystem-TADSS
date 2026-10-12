using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TADSS.Application;
using TADSS.Infrastructure;
using TADSS.Runtime.Api.Composition;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Gói đồ thị DI lõi của runtime như composition root dựng (<c>AddApplication</c> → <c>AddInfrastructure</c> →
/// <c>AddStrategies</c>) để test dựng nhanh một service provider thật. Không gồm <c>AddPresentation</c> (HTTP/Swagger).
/// </summary>
internal static class RuntimeServiceProviderBuilder
{
    /// <summary>
    /// Đăng ký ba lớp lõi theo đúng thứ tự của <c>Program.cs</c>. Caller phải cấp chuỗi kết nối
    /// (xem <c>TestConnectionStrings</c>) vì health check PostgreSQL cần nó. <paramref name="environmentName"/> là môi trường
    /// của host giả lập (mặc định <c>Production</c>, không bao giờ bọc decorator tiêm lỗi).
    /// </summary>
    public static IServiceCollection AddRuntimeCore(this IServiceCollection services, IConfiguration configuration, string environmentName = "Production")
    {
        // Như host thật: IConfiguration có trong DI (DatabaseOptions đọc chuỗi kết nối từ cấu hình cuối cùng)
        services.TryAddSingleton(configuration);
        var environment = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environmentName }).Environment;
        return services
            .AddApplication(configuration)
            .AddInfrastructure(configuration)
            .AddStrategies(configuration, environment);
    }
}
