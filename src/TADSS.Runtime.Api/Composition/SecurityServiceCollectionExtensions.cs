using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Options;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Đăng ký DI của các thành phần bảo mật (xác thực API key; rate limit và giới hạn kích thước sẽ thêm vào đây).
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Nối <see cref="SecurityOptions"/> (section <c>Tadss:Security</c>) với validator và <c>ValidateOnStart</c>, đăng ký <see cref="ApiKeyVerifier"/>.
    /// </summary>
    public static IServiceCollection AddTadssSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        // Cấu hình API key: sai hoặc thiếu (ngoài Development/Testing) thì ứng dụng không khởi động
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();

        services.AddSingleton<ApiKeyVerifier>();

        return services;
    }
}
