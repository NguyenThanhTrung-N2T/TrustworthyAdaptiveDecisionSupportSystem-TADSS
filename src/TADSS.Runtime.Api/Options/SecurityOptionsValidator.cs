using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Kiểm tra <see cref="SecurityOptions"/> khi khởi động (fail-fast): <c>ClientId</c> hợp lệ và duy nhất, <c>KeySha256</c> là hex 64 ký tự và duy nhất.
/// Ngoài <c>Development</c>/<c>Testing</c>, <c>ApiKeys</c> không được rỗng và không được chứa mã băm của khóa local công khai
/// (<see cref="LocalDevelopmentKeySha256"/>). Thông báo chỉ nêu chỉ số phần tử và tên trường, không bao giờ chứa mã băm.
/// </summary>
public sealed partial class SecurityOptionsValidator(IHostEnvironment environment) : IValidateOptions<SecurityOptions>
{
    /// <summary>
    /// SHA-256 (hex thường) của khóa local công khai <c>tadss-local-dev-key</c> (docker-compose E-LOCAL, ghi trong README).
    /// Ai cũng biết khóa này nên ngoài <c>Development</c>/<c>Testing</c> mã băm này bị từ chối.
    /// </summary>
    public const string LocalDevelopmentKeySha256 = "0472e99d9ccd889b6e6781cf0920a9b39910c1c430393d62839465c4901a6268";

    private const string Prefix = SecurityOptions.SectionName + ":ApiKeys";

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ClientIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
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

        var clientIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < apiKeys.Count; i++)
        {
            ValidateEntry(apiKeys[i], i, relaxed, clientIds, hashes, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
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
