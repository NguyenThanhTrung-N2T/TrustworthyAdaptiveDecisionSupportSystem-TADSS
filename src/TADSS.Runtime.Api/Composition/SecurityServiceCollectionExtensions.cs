using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Common;
using TADSS.Runtime.Api.ErrorHandling;
using TADSS.Runtime.Api.Options;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Đăng ký DI của các thành phần bảo mật: xác thực API key, header chuyển tiếp (F-10), HSTS và giới hạn tốc độ theo client (SEC-1) và tắt header <c>Server</c> của Kestrel.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Nối <see cref="SecurityOptions"/> (section <c>Tadss:Security</c>) với validator và <c>ValidateOnStart</c>, đăng ký <see cref="ApiKeyVerifier"/>,
    /// cấu hình <see cref="ForwardedHeadersOptions"/> (chỉ có tác dụng khi <see cref="SecurityOptions.TrustForwardedHeaders"/> bật, xem <c>UseTadssRequestPipeline</c>), HSTS mặc định
    /// token bucket theo client (<see cref="RateLimitingOptions"/>) và <see cref="KestrelServerOptions.AddServerHeader"/> = <c>false</c> (không lộ <c>Server: Kestrel</c>).
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

        AddClientRateLimiting(services);

        return services;
    }

    /// <summary>
    /// SEC-1: token bucket theo <c>ClientId</c> đã xác thực (client chưa xác định dùng chung phân vùng <c>anonymous</c>); <c>/health/*</c> không giới hạn.
    /// Không xếp hàng chờ (<c>QueueLimit = 0</c>): hết token là từ chối ngay bằng 429 <c>RATE_LIMITED</c> qua <see cref="ProblemResponseWriter"/>.
    /// Tham số đọc từ <see cref="SecurityOptions.RateLimiting"/> lúc dựng pipeline (đã qua validator khi khởi động).
    /// </summary>
    private static void AddClientRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<SecurityOptions>>((limiter, security) =>
        {
            var settings = security.Value.RateLimiting;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                SecurityPaths.IsHealth(context.Request.Path)
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetTokenBucketLimiter(
                        ApiClientIdentity.Get(context) ?? "anonymous",
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = settings.PermitLimit,
                            TokensPerPeriod = settings.TokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(settings.ReplenishmentPeriodSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            limiter.OnRejected = (rejected, cancellationToken) =>
            {
                // Retry-After: trần số giây theo metadata của lease (thời gian đến khi đủ token), tối thiểu 1; không có metadata thì một chu kỳ nạp
                var retryAfterSeconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : settings.ReplenishmentPeriodSeconds;
                return new ValueTask(ProblemResponseWriter.WriteAsync(
                    rejected.HttpContext,
                    new ProblemResponse(
                        StatusCodes.Status429TooManyRequests,
                        ErrorCodes.RateLimited,
                        "Too Many Requests",
                        string.Create(CultureInfo.InvariantCulture, $"Request rate limit exceeded. Retry after {Math.Max(1, retryAfterSeconds)} second(s)."),
                        Math.Max(1, retryAfterSeconds)),
                    cancellationToken));
            };
        });
    }
}
