namespace TADSS.Infrastructure.Options;

/// <summary>
/// Tên các nhà cung cấp cache hợp lệ của <see cref="CacheOptions.Provider"/> (so sánh không phân biệt hoa thường).
/// </summary>
public static class CacheProviders
{
    /// <summary>Redis (L2) kèm memory fallback.</summary>
    public const string Redis = "Redis";

    /// <summary>Chỉ dùng memory (L1).</summary>
    public const string Memory = "Memory";

    /// <summary>
    /// So sánh không phân biệt hoa thường: <paramref name="provider"/> có phải <see cref="Redis"/> không.
    /// </summary>
    public static bool IsRedis(string? provider) =>
        string.Equals(provider, Redis, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// So sánh không phân biệt hoa thường: <paramref name="provider"/> có phải <see cref="Memory"/> không.
    /// </summary>
    public static bool IsMemory(string? provider) =>
        string.Equals(provider, Memory, StringComparison.OrdinalIgnoreCase);
}
