namespace TADSS.Runtime.Api.Security;

/// <summary>
/// Danh tính client đã xác thực bằng API key, lưu trong <see cref="HttpContext.Items"/> cho các bước sau (rate limit, audit).
/// </summary>
public static class ApiClientIdentity
{
    private const string ItemKey = "Tadss.ApiClientId";

    /// <summary>Gắn <paramref name="clientId"/> vào request hiện tại.</summary>
    public static void Set(HttpContext context, string clientId) => context.Items[ItemKey] = clientId;

    /// <summary>Lấy <c>ClientId</c> đã xác thực của request; <c>null</c> khi request chưa/không cần xác thực (health, Swagger ở Development).</summary>
    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}
