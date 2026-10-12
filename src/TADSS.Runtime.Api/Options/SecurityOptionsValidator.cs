using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Kiểm tra <see cref="SecurityOptions"/> khi khởi động (fail-fast): <c>MaxRequestBodyBytes</c> và các tham số <c>RateLimiting</c> trong khoảng cho phép, <c>ClientId</c> hợp lệ, duy nhất và không trùng tên dành riêng (<c>health</c>, <c>anonymous</c>), <c>KeySha256</c> là hex 64 ký tự và duy nhất.
/// Ngoài <c>Development</c>/<c>Testing</c>, <c>ApiKeys</c> không được rỗng và không được chứa mã băm của khóa local công khai
/// (<see cref="LocalDevelopmentKeySha256"/>). Thông báo chỉ nêu chỉ số phần tử và tên trường, không bao giờ chứa mã băm.
/// </summary>
public sealed partial class SecurityOptionsValidator(IHostEnvironment environment) : IValidateOptions<SecurityOptions>
{
    /// <summary>
    /// SHA-256 (hex thường) của khóa local công khai <c>tadss-local-dev-key</c> (dùng ở Development qua appsettings.Development.json, ghi trong README).
    /// Ai cũng biết khóa này nên ngoài <c>Development</c>/<c>Testing</c> mã băm này bị từ chối.
    /// </summary>
    public const string LocalDevelopmentKeySha256 = "0472e99d9ccd889b6e6781cf0920a9b39910c1c430393d62839465c4901a6268";

    private const string Prefix = SecurityOptions.SectionName + ":ApiKeys";

    // Trùng khóa phân vùng của rate limiter ("health" cho /health/*, "anonymous" cho request chưa xác định danh) sẽ dùng chung xô/limiter với client thật
    private static readonly HashSet<string> ReservedClientIds = new(["health", "anonymous"], StringComparer.OrdinalIgnoreCase);

    // Dùng \A..\z thay vì ^..$: $ của .NET chấp nhận thêm một ký tự xuống dòng cuối chuỗi, lọt vào log/tag/phân vùng và làm verifier bỏ mục
    [GeneratedRegex(@"\A[A-Za-z0-9._-]{1,64}\z")]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex(@"\A[0-9a-fA-F]{64}\z")]
    private static partial Regex KeySha256Pattern();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        var failures = new List<string>();
        var relaxed = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var apiKeys = options.ApiKeys;

        if (!relaxed && apiKeys.Count == 0)
        {
            failures.Add($"{Prefix} không được rỗng ngoài môi trường Development/Testing: cấp ít nhất một {{ ClientId, KeySha256 }}.");
        }

        if (options.MaxRequestBodyBytes is < SecurityOptions.MinRequestBodyBytes or > SecurityOptions.MaxRequestBodyBytesLimit)
        {
            failures.Add($"{SecurityOptions.SectionName}:{nameof(options.MaxRequestBodyBytes)} phải nằm trong khoảng {SecurityOptions.MinRequestBodyBytes}..{SecurityOptions.MaxRequestBodyBytesLimit} byte.");
        }

        ValidateRateLimiting(options.RateLimiting, failures);

        var clientIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < apiKeys.Count; i++)
        {
            ValidateEntry(apiKeys[i], i, relaxed, clientIds, hashes, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRateLimiting(RateLimitingOptions? rateLimiting, List<string> failures)
    {
        const string prefix = SecurityOptions.SectionName + ":" + nameof(SecurityOptions.RateLimiting);
        if (rateLimiting is null)
        {
            failures.Add($"{prefix} không được rỗng.");
            return;
        }

        if (rateLimiting.PermitLimit is < 1 or > RateLimitingOptions.MaxPermitLimit)
        {
            failures.Add($"{prefix}:{nameof(rateLimiting.PermitLimit)} phải nằm trong khoảng 1..{RateLimitingOptions.MaxPermitLimit}.");
        }

        if (rateLimiting.TokensPerPeriod < 1 || rateLimiting.TokensPerPeriod > rateLimiting.PermitLimit)
        {
            failures.Add($"{prefix}:{nameof(rateLimiting.TokensPerPeriod)} phải nằm trong khoảng 1..{nameof(rateLimiting.PermitLimit)}.");
        }

        if (rateLimiting.ReplenishmentPeriodSeconds is < 1 or > RateLimitingOptions.MaxReplenishmentPeriodSeconds)
        {
            failures.Add($"{prefix}:{nameof(rateLimiting.ReplenishmentPeriodSeconds)} phải nằm trong khoảng 1..{RateLimitingOptions.MaxReplenishmentPeriodSeconds} giây.");
        }
    }

    private static void ValidateEntry(
        ApiKeyClientOptions? entry, int index, bool relaxed, Dictionary<string, int> clientIds, Dictionary<string, int> hashes, List<string> failures)
    {
        if (entry is null)
        {
            failures.Add($"{Prefix}:{index} không được rỗng.");
            return;
        }

        var clientId = entry.ClientId ?? string.Empty;
        var clientIdField = $"{Prefix}:{index}:{nameof(entry.ClientId)}";
        if (!ClientIdPattern().IsMatch(clientId))
        {
            failures.Add($"{clientIdField} phải gồm 1..64 ký tự A-Z a-z 0-9 . _ -.");
        }
        else if (ReservedClientIds.Contains(clientId))
        {
            failures.Add($"{clientIdField} là tên dành riêng của bộ giới hạn tốc độ, hãy chọn ClientId khác.");
        }
        else if (!clientIds.TryAdd(clientId, index))
        {
            failures.Add($"{clientIdField} trùng với {Prefix}:{clientIds[clientId]}:{nameof(entry.ClientId)} (không phân biệt hoa thường).");
        }

        var keySha256 = entry.KeySha256 ?? string.Empty;
        var hashField = $"{Prefix}:{index}:{nameof(entry.KeySha256)}";
        if (!KeySha256Pattern().IsMatch(keySha256))
        {
            failures.Add($"{hashField} phải là chuỗi hex 64 ký tự (SHA-256 của API key).");
            return;
        }

        if (!hashes.TryAdd(keySha256, index))
        {
            failures.Add($"{hashField} trùng với {Prefix}:{hashes[keySha256]}:{nameof(entry.KeySha256)}.");
        }

        if (!relaxed && string.Equals(keySha256, LocalDevelopmentKeySha256, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{hashField} là khóa local công khai, không được dùng ngoài môi trường Development/Testing.");
        }
    }
}
