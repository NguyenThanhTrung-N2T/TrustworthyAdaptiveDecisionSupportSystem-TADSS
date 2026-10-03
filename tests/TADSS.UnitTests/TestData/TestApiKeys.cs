using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TADSS.UnitTests.TestData;

/// <summary>
/// API key giả dùng cho test (chuỗi cố định trong mã test, không phải secret) và cách cấu hình chúng cho host thử nghiệm:
/// <c>Tadss:Security:ApiKeys:{i}:ClientId/KeySha256</c>. Cả hai factory (<c>DecisionsApiFactory</c>, <c>TadssWebApplicationFactory</c>) dùng chung
/// để mọi <see cref="HttpClient"/> mặc định gửi <see cref="DefaultKey"/>; test cần "không có khóa" tạo client bằng <see cref="CreateClientWithoutKey"/>.
/// </summary>
internal static class TestApiKeys
{
    /// <summary>Tên header API key.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>Khóa giả của client mặc định.</summary>
    public const string DefaultKey = "test-api-key-default-0001";

    /// <summary><c>ClientId</c> của client mặc định.</summary>
    public const string DefaultClientId = "test-client-default";

    /// <summary>Khóa giả của client thứ hai (kiểm tra phân biệt client).</summary>
    public const string SecondKey = "test-api-key-second-0002";

    /// <summary><c>ClientId</c> của client thứ hai.</summary>
    public const string SecondClientId = "test-client-second";

    /// <summary>Một client giả: <see cref="ClientId"/> và khóa gốc (chỉ có trong mã test).</summary>
    public sealed record Client(string ClientId, string Key)
    {
        /// <summary>SHA-256 (UTF-8, hex thường) của <see cref="Key"/>: giá trị cấu hình <c>KeySha256</c>.</summary>
        public string KeySha256 => Sha256Hex(Key);
    }

    /// <summary>Client mặc định.</summary>
    public static Client Default { get; } = new(DefaultClientId, DefaultKey);

    /// <summary>Client thứ hai.</summary>
    public static Client Second { get; } = new(SecondClientId, SecondKey);

    /// <summary>Cấu hình mặc định của host thử nghiệm: chỉ client mặc định.</summary>
    public static IReadOnlyList<Client> DefaultClients { get; } = [Default];

    /// <summary>SHA-256 (UTF-8) của <paramref name="value"/>, hex thường.</summary>
    public static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Cặp khóa/giá trị <c>Tadss:Security:ApiKeys:{i}:*</c> cho <c>AddInMemoryCollection</c>.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> ToConfiguration(IReadOnlyList<Client> clients)
    {
        for (var i = 0; i < clients.Count; i++)
        {
            yield return new($"Tadss:Security:ApiKeys:{i}:ClientId", clients[i].ClientId);
            yield return new($"Tadss:Security:ApiKeys:{i}:KeySha256", clients[i].KeySha256);
        }
    }

    /// <summary>Đặt khóa mặc định làm header mặc định của <paramref name="client"/>.</summary>
    public static void ApplyDefaultKey(HttpClient client) => client.DefaultRequestHeaders.TryAddWithoutValidation(HeaderName, DefaultKey);

    /// <summary>Tạo client của <paramref name="factory"/> nhưng không mang header API key (cho test 401).</summary>
    public static HttpClient CreateClientWithoutKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(HeaderName);
        return client;
    }
}
