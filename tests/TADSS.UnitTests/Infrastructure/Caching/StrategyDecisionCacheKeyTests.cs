using FluentAssertions;
using TADSS.Runtime.Api.Infrastructure.Caching;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// UT-13: khóa cache v2 (<see cref="StrategyDecisionCacheKey"/>) — đúng định dạng spec 4.2.3
/// <c>tadss:v2:{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}</c> và mọi thành phần phải không rỗng.
/// </summary>
public class StrategyDecisionCacheKeyTests
{
    private const string Fingerprint = "a1b2c3d4e5f60718";
    private const string RequestHash = "9f86d081884c7d659a2feaa0c55ad015";

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Build_AllParts_FollowsSpecFormat()
    {
        var key = StrategyDecisionCacheKey.Build("Default", "2.0.0", Fingerprint, RequestHash);

        key.Should().Be($"tadss:v2:Default:2.0.0:{Fingerprint}:{RequestHash}");
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Build_DifferentParts_ProduceDifferentKeys()
    {
        var baseline = StrategyDecisionCacheKey.Build("Default", "2.0.0", Fingerprint, RequestHash);

        StrategyDecisionCacheKey.Build("MockAlternative", "2.0.0", Fingerprint, RequestHash).Should().NotBe(baseline);
        StrategyDecisionCacheKey.Build("Default", "2.0.1", Fingerprint, RequestHash).Should().NotBe(baseline);
        StrategyDecisionCacheKey.Build("Default", "2.0.0", "ffffffffffffffff", RequestHash).Should().NotBe(baseline);
        StrategyDecisionCacheKey.Build("Default", "2.0.0", Fingerprint, "00000000000000000000000000000000").Should().NotBe(baseline);
    }

    public static TheoryData<string, string, string, string> EmptyPartCases => new()
    {
        { "", "2.0.0", Fingerprint, RequestHash },
        { "Default", "", Fingerprint, RequestHash },
        { "Default", "2.0.0", "", RequestHash },
        { "Default", "2.0.0", Fingerprint, "" },
        { " ", "2.0.0", Fingerprint, RequestHash },
        { "Default", "2.0.0", Fingerprint, "   " },
    };

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [MemberData(nameof(EmptyPartCases))]
    public void Build_EmptyPart_Throws_Theory(string name, string version, string fingerprint, string hash)
    {
        var act = () => StrategyDecisionCacheKey.Build(name, version, fingerprint, hash);

        act.Should().Throw<ArgumentException>();
    }
}
