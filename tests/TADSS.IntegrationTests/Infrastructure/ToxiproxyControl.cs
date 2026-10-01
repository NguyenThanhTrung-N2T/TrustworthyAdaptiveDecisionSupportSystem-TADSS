using System.Net.Http.Json;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Client tối giản cho REST API của Toxiproxy (cổng điều khiển 8474). Module Testcontainers.Toxiproxy
/// chỉ dựng container nên test tự gọi API để tạo proxy, bật/tắt và reset.
/// </summary>
public sealed class ToxiproxyControl : IDisposable
{
    private readonly HttpClient _http;

    public ToxiproxyControl(Uri apiBaseAddress)
    {
        _http = new HttpClient { BaseAddress = apiBaseAddress };
    }

    public async Task CreateProxyAsync(string name, int listenPort, string upstream)
    {
        using var response = await _http.PostAsJsonAsync("/proxies",
            new { name, listen = $"0.0.0.0:{listenPort}", upstream, enabled = true });
        response.EnsureSuccessStatusCode();
    }

    public async Task SetEnabledAsync(string name, bool enabled)
    {
        using var response = await _http.PostAsJsonAsync($"/proxies/{name}", new { enabled });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Bật lại mọi proxy và gỡ mọi toxic.</summary>
    public async Task ResetAsync()
    {
        using var response = await _http.PostAsync("/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
