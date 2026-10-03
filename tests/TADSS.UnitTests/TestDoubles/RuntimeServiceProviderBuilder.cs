using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// (xem <c>TestConnectionStrings</c>) vì health check PostgreSQL cần nó.
    /// </summary>
    public static IServiceCollection AddRuntimeCore(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddApplication(configuration)
            .AddInfrastructure(configuration)
            .AddStrategies(configuration);
    }
}
