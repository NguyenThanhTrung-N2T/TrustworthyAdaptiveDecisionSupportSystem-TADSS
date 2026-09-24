namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Builder for strongly-typed cache keys in the TADSS Decision Runtime Engine.
/// Generates deterministic, scoped keys conforming to RFC 8785 request hashing.
/// </summary>
public static class CacheKeyBuilder
{
    private const string DecisionHashPrefix = "decision:hash";

    /// <summary>
    /// Builds a deterministic cache key scoped by request hash, strategy name, and strategy version.
    /// Format: decision:hash:{requestHash}:{strategyName}:v{strategyVersion}
    /// </summary>
    public static string BuildDecisionHashKey(string requestHash, string strategyName, string strategyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyVersion);

        return $"{DecisionHashPrefix}:{requestHash}:{strategyName}:v{strategyVersion}";
    }
}
