using FluentAssertions;
using TADSS.Runtime.Api.Options;
using TADSS.Runtime.Api.Security;
using TADSS.UnitTests.TestData;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-4: <see cref="ApiKeyVerifier"/> băm khóa nhận được rồi so với mã băm cấu hình (không phân biệt hoa thường) và trả đúng <c>ClientId</c>;
/// khóa null, rỗng hoặc dài hơn <see cref="ApiKeyVerifier.MaxKeyLength"/> luôn bị từ chối.
/// </summary>
public class ApiKeyVerifierTests
{
    private const string KeyA = "unit-test-key-a";
    private const string KeyB = "unit-test-key-b";

    private static readonly string BoundaryKey = new('k', ApiKeyVerifier.MaxKeyLength);

    private static ApiKeyVerifier CreateVerifier() =>
        new(MsOptions.Create(new SecurityOptions
        {
            ApiKeys =
            [
                // Mã băm của client A viết HOA để kiểm tra so sánh không phân biệt hoa thường
                new ApiKeyClientOptions { ClientId = "client-a", KeySha256 = TestApiKeys.Sha256Hex(KeyA).ToUpperInvariant() },
                new ApiKeyClientOptions { ClientId = "client-b", KeySha256 = TestApiKeys.Sha256Hex(KeyB) },
                new ApiKeyClientOptions { ClientId = "client-boundary", KeySha256 = TestApiKeys.Sha256Hex(BoundaryKey) }
            ]
        }));

    public static TheoryData<string?, bool, string> Cases => new()
    {
        { KeyA, true, "client-a" },
        { KeyB, true, "client-b" },
        { "unit-test-key-c", false, "" },
        { "Unit-test-key-a", false, "" },
        { "", false, "" },
        { null, false, "" },
        { new string('k', ApiKeyVerifier.MaxKeyLength + 1), false, "" },
        { BoundaryKey, true, "client-boundary" }
    };

    /// <summary>Đúng khóa (mã băm cấu hình viết hoa), sai một ký tự, rỗng, null, 257 ký tự, hai client, và khóa đúng 256 ký tự (biên).</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("CatalogId", "SEC-4")]
    public void TryAuthenticate_Theory(string? presentedKey, bool expected, string expectedClientId)
    {
        var verifier = CreateVerifier();

        var result = verifier.TryAuthenticate(presentedKey, out var clientId);

        result.Should().Be(expected);
        clientId.Should().Be(expectedClientId);
    }

    /// <summary>Mã băm cấu hình sai hình thức (63 ký tự, thừa ký tự xuống dòng cuối, ký tự không phải hex) làm constructor ném lỗi nêu chỉ số, không chứa mã băm; không âm thầm bỏ mục.</summary>
    [Theory]
    [InlineData(63, "")]
    [InlineData(64, "\n")]
    [InlineData(63, "g")]
    [Trait("CatalogId", "SEC-4")]
    public void Constructor_MalformedConfiguredHash_Throws(int hexLength, string suffix)
    {
        var hash = TestApiKeys.Sha256Hex(KeyA)[..hexLength] + suffix;
        var options = MsOptions.Create(new SecurityOptions { ApiKeys = [new ApiKeyClientOptions { ClientId = "client-a", KeySha256 = hash }] });

        var create = () => new ApiKeyVerifier(options);

        var failure = create.Should().Throw<InvalidOperationException>().Which;
        failure.Message.Should().Contain("Tadss:Security:ApiKeys:0:KeySha256").And.NotContain(hash[..63]);
    }

    /// <summary>Cấu hình không có client nào: mọi khóa đều bị từ chối.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void TryAuthenticate_NoClientsConfigured_RejectsEveryKey()
    {
        var verifier = new ApiKeyVerifier(MsOptions.Create(new SecurityOptions()));

        verifier.TryAuthenticate(KeyA, out var clientId).Should().BeFalse();
        clientId.Should().BeEmpty();
    }
}
