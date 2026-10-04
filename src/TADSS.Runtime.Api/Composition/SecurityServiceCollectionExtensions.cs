using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Options;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Đăng ký DI của các thành phần bảo mật: xác thực API key, header chuyển tiếp (F-10), HSTS và tắt header <c>Server</c> của Kestrel (rate limit thêm ở Task 7).
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Nối <see cref="SecurityOptions"/> (section <c>Tadss:Security</c>) với validator và <c>ValidateOnStart</c>, đăng ký <see cref="ApiKeyVerifier"/>,
    /// cấu hình <see cref="ForwardedHeadersOptions"/> (chỉ có tác dụng khi <see cref="SecurityOptions.TrustForwardedHeaders"/> bật, xem <c>UseTadssRequestPipeline</c>), HSTS mặc định
    /// và <see cref="KestrelServerOptions.AddServerHeader"/> = <c>false</c> (không lộ <c>Server: Kestrel</c>).
    /// </summary>
    public static IServiceCollection AddTadssSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        // Cấu hình API key: sai hoặc thiếu (ngoài Development/Testing) thì ứng dụng không khởi động
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();

        services.AddSingleton<ApiKeyVerifier>();

        // F-10: tin X-Forwarded-* từ mọi proxy (xóa KnownNetworks/KnownProxies như SmashCourt-BE) vì chỉ bật khi chỉ proxy chạm được ứng dụng
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });
        services.AddHsts(_ => { });
        services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);

        return services;
    }
}
