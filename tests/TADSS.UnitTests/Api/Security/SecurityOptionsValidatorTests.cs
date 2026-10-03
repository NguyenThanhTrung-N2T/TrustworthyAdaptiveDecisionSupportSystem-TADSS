using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-4: <see cref="SecurityOptionsValidator"/> chặn cấu hình API key sai ngay khi khởi động (fail-fast); ngoài <c>Development</c>/<c>Testing</c>
/// còn bắt buộc có khóa và cấm mã băm của khóa local công khai. Thông báo không bao giờ chứa mã băm.
/// </summary>
public class SecurityOptionsValidatorTests
{
    private const string LocalDevKey = "tadss-local-dev-key";
    private static readonly string GoodHash = TestApiKeys.Sha256Hex("unit-test-good-key");
    private static readonly string OtherHash = TestApiKeys.Sha256Hex("unit-test-other-key");
    private static readonly string ShortHash = GoodHash[..63];

    private static SecurityOptionsValidator CreateValidator(string environmentName) =>
        new(Mock.Of<IHostEnvironment>(environment => environment.EnvironmentName == environmentName));

    private static ApiKeyClientOptions Client(string clientId, string keySha256) => new() { ClientId = clientId, KeySha256 = keySha256 };

    public static TheoryData<string, ApiKeyClientOptions[], bool> Cases => new()
    {
        { "Staging", [], false },
        { "Staging", [Client("local", SecurityOptionsValidator.LocalDevelopmentKeySha256)], false },
        { "Staging", [Client("local", SecurityOptionsValidator.LocalDevelopmentKeySha256.ToUpperInvariant())], false },
        { "Development", [], true },
        { "Testing", [], true },
        { "Development", [Client("local", SecurityOptionsValidator.LocalDevelopmentKeySha256)], true },
        { "Staging", [Client("be", GoodHash)], true },
        { "Staging", [Client("be", GoodHash), Client("BE", OtherHash)], false },
        { "Staging", [Client("be", GoodHash), Client("fi", GoodHash.ToUpperInvariant())], false },
        { "Staging", [Client("be", ShortHash)], false },
        { "Staging", [Client("be client", GoodHash)], false },
        { "Staging", [Client("", GoodHash)], false },
        { "Staging", [Client(new string('a', 65), GoodHash)], false },
        { "Production", [Client("be", GoodHash), Client("fi", OtherHash)], true }
    };

    /// <summary>Staging rỗng / có khóa local, Development rỗng, ClientId trùng, hex 63 ký tự, ký tự lạ trong ClientId... mọi message không chứa mã băm.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("CatalogId", "SEC-4")]
    public void Validate_Theory(string environmentName, ApiKeyClientOptions[] apiKeys, bool expectedValid)
    {
        var options = new SecurityOptions { ApiKeys = [.. apiKeys] };

        var result = CreateValidator(environmentName).Validate(null, options);

        result.Succeeded.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("Tadss:Security:ApiKeys");
            foreach (var hash in apiKeys.Select(client => client.KeySha256).Where(hash => !string.IsNullOrEmpty(hash)))
            {
                result.FailureMessage.Should().NotContainEquivalentOf(hash);
            }
        }
    }

    /// <summary>Message nêu chỉ số phần tử và tên trường để người vận hành sửa đúng chỗ.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void Validate_InvalidSecondEntry_MessageNamesIndexAndField()
    {
        var options = new SecurityOptions { ApiKeys = [Client("be", GoodHash), Client("fi", ShortHash)] };

        var result = CreateValidator("Staging").Validate(null, options);

        result.FailureMessage.Should().Contain("Tadss:Security:ApiKeys:1:KeySha256");
    }

    /// <summary>Hằng mã băm của khóa local công khai đúng là SHA-256 của <c>tadss-local-dev-key</c> (tính lại lúc chạy test).</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void LocalDevelopmentKeySha256_IsHashOfLocalDevKey()
    {
        SecurityOptionsValidator.LocalDevelopmentKeySha256.Should().Be(TestApiKeys.Sha256Hex(LocalDevKey));
    }

    /// <summary>Host <c>Staging</c> không có API key không khởi động được: <see cref="OptionsValidationException"/> nêu khóa cấu hình.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void Host_StagingWithoutApiKeys_FailsOnStart()
    {
        using var factory = new DecisionsApiFactory { HostEnvironment = "Staging", ApiKeys = [] };

        var start = () => factory.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage("*Tadss:Security:ApiKeys*");
    }

    /// <summary>Host <c>Staging</c> chỉ có khóa local công khai không khởi động được; message không chứa mã băm.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void Host_StagingWithPublicLocalKey_FailsOnStart()
    {
        var localClient = new TestApiKeys.Client("local", LocalDevKey);
        using var factory = new DecisionsApiFactory { HostEnvironment = "Staging", ApiKeys = [localClient] };

        var start = () => factory.CreateClient();

        var failure = start.Should().Throw<OptionsValidationException>().Which;
        failure.Message.Should().Contain("Tadss:Security:ApiKeys:0:KeySha256").And.NotContainEquivalentOf(localClient.KeySha256);
    }

    /// <summary>Host <c>Staging</c> có một khóa hợp lệ khởi động bình thường.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public void Host_StagingWithValidKey_Starts()
    {
        using var factory = new DecisionsApiFactory { HostEnvironment = "Staging" };

        var start = () => factory.CreateClient();

        start.Should().NotThrow();
    }
}
