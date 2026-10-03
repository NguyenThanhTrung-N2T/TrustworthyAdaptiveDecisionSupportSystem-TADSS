using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Options;

namespace TADSS.Runtime.Api.Security;

/// <summary>
/// Xác thực API key: băm SHA-256 khóa nhận được rồi so sánh thời gian cố định với mã băm của <b>mọi</b> client (không thoát sớm),
/// nên thời gian xử lý không tiết lộ khóa đúng đến đâu hay client nào khớp. Không bao giờ log khóa hoặc mã băm của khóa.
/// </summary>
public sealed class ApiKeyVerifier
{
    /// <summary>Độ dài tối đa (ký tự) của khóa nhận được; dài hơn bị từ chối trước khi băm để không tốn CPU cho input bất thường.</summary>
    public const int MaxKeyLength = 256;

    private const int Sha256Bytes = 32;

    private readonly (string ClientId, byte[] Hash)[] _clients;

    /// <summary>Chuẩn hóa mã băm cấu hình (hex, không phân biệt hoa thường) về byte một lần; mục sai hình thức bị bỏ qua (validator đã chặn lúc khởi động).</summary>
    public ApiKeyVerifier(IOptions<SecurityOptions> options)
    {
        var clients = new List<(string ClientId, byte[] Hash)>();
        foreach (var entry in options.Value.ApiKeys)
        {
            if (TryParseHash(entry.KeySha256, out var hash))
            {
                clients.Add((entry.ClientId, hash));
            }
        }

        _clients = clients.ToArray();
    }

    /// <summary>
    /// Thử xác thực <paramref name="presentedKey"/>.
    /// </summary>
    /// <param name="presentedKey">Giá trị header <c>X-Api-Key</c>.</param>
    /// <param name="clientId"><c>ClientId</c> khớp; rỗng khi không khớp.</param>
    /// <returns><c>true</c> khi khóa khớp một client.</returns>
    public bool TryAuthenticate(string? presentedKey, out string clientId)
    {
        clientId = string.Empty;
        if (string.IsNullOrEmpty(presentedKey) || presentedKey.Length > MaxKeyLength)
        {
            return false;
        }

        Span<byte> presentedHash = stackalloc byte[Sha256Bytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey), presentedHash);

        var found = false;
        foreach (var (candidateId, hash) in _clients)
        {
            // Không break: luôn so với mọi mục để thời gian không phụ thuộc vị trí client khớp
            if (CryptographicOperations.FixedTimeEquals(presentedHash, hash) && !found)
            {
                clientId = candidateId;
                found = true;
            }
        }

        return found;
    }

    private static bool TryParseHash(string? hex, out byte[] hash)
    {
        hash = [];
        if (hex is not { Length: Sha256Bytes * 2 })
        {
            return false;
        }

        try
        {
            hash = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
