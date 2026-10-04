using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// Hàm dùng chung của test bảo mật HTTP (SEC-2, SEC-3, UT-14): dựng request POST đánh giá, dựng host theo môi trường/cấu hình
/// (không nạp <c>appsettings.Development.json</c> cục bộ của người chạy) và tạo body có kích thước byte chính xác.
/// </summary>
internal static class SecurityTestHost
{
    public const string DecisionsPath = "/api/v2/decisions";
    public const string DevelopmentSettingsFile = "appsettings.Development.json";

    /// <summary>
    /// POST đánh giá với <c>Content-Length</c> đặt tường minh. <paramref name="apiKey"/> <c>null</c> thì không gửi khóa
    /// (dùng cùng client của <see cref="TestApiKeys.CreateClientWithoutKey"/>); khóa nếu có được thêm riêng cho request này.
    /// </summary>
    public static HttpRequestMessage Post(string body, string? apiKey = null, string mediaType = "application/json", string? correlationId = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        content.Headers.ContentLength = bytes.Length;
        var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath) { Content = content };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, "booking-security-1");
        if (apiKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.ApiKey, apiKey);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        }

        return request;
    }

    /// <summary>JSON hợp lệ của contract, đệm khoảng trắng cuối cho đủ đúng <paramref name="totalBytes"/> byte (UTF-8).</summary>
    public static string RequestJsonOfSize(int totalBytes)
    {
        var baseBytes = Encoding.UTF8.GetByteCount(ContractSamples.RequestJson);
        if (totalBytes < baseBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), "Nhỏ hơn kích thước của request mẫu.");
        }

        return ContractSamples.RequestJson + new string(' ', totalBytes - baseBytes);
    }

    /// <summary>
    /// Dẫn xuất host từ <paramref name="baseFactory"/> với môi trường <paramref name="environment"/> và cấu hình bổ sung; bỏ nguồn
    /// <c>appsettings.Development.json</c> để test không phụ thuộc cấu hình cục bộ.
    /// </summary>
    public static WebApplicationFactory<Program> WithEnvironment(
        WebApplicationFactory<Program> baseFactory, string environment, params (string Key, string Value)[] settings) =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var developmentSources = configuration.Sources
                    .OfType<JsonConfigurationSource>()
                    .Where(source => source.Path == DevelopmentSettingsFile)
                    .ToList();
                foreach (var source in developmentSources)
                {
                    configuration.Sources.Remove(source);
                }

                configuration.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)));
            });
        });
}
