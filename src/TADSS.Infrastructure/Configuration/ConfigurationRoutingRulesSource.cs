using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using TADSS.Application.Abstractions.Configuration;
using TADSS.Application.Options;

namespace TADSS.Infrastructure.Configuration;

/// <summary>
/// Adapter của <see cref="IRoutingRulesSource"/> trên <see cref="IConfiguration"/> lấy từ DI (cấu hình cuối cùng của host, gồm override của host test).
/// <see cref="Read"/> bind section <see cref="StrategyRoutingOptions.SectionName"/> và báo lỗi mà binder của .NET nuốt (<see cref="RoutingConfigurationBinder"/>);
/// <see cref="Subscribe"/> đăng ký <see cref="ChangeToken.OnChange(Func{IChangeToken?}, Action)"/> trên reload token của cấu hình. Singleton, không giữ trạng thái.
/// </summary>
public sealed class ConfigurationRoutingRulesSource : IRoutingRulesSource
{
    private readonly IConfiguration _configuration;

    /// <summary>Nguồn định tuyến đọc và theo dõi <paramref name="configuration"/>.</summary>
    /// <param name="configuration">Cấu hình cuối cùng của host.</param>
    public ConfigurationRoutingRulesSource(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public StrategyRoutingOptions Read(ICollection<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return RoutingConfigurationBinder.Bind(_configuration, failures);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        return ChangeToken.OnChange(_configuration.GetReloadToken, onChanged);
    }
}
