using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.V2.Services.Strategies;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Services;

/// <summary>
/// UT-04 (phần resolver): <see cref="StrategyRegistry"/> tra strategy v2 theo tên không phân biệt hoa thường, phát hiện
/// trùng tên và tên kích hoạt sai ngay lúc khởi tạo (fail-fast), và có thể dựng chỉ với một strategy (test double).
/// </summary>
public class StrategyRegistryTests
{
    private static RegisteredStrategy CreateDefault() =>
        StrategyRegistrations.CreateDefault(new ConfigurationBuilder().Build(), V2TestData.StandardCalendar());

    private static RegisteredStrategy CreateMock() =>
        StrategyRegistrations.CreateMockAlternative(new ConfigurationBuilder().Build(), V2TestData.StandardCalendar());

    private static StrategyRegistry Build(string active, params RegisteredStrategy[] strategies) =>
        new(strategies, Options.Create(new StrategyOptions { ActiveStrategyName = active }), NullLogger<StrategyRegistry>.Instance);

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("DefaultDecisionStrategy", "DefaultDecisionStrategy")]
    [InlineData("MockAlternativeDecisionStrategy", "MockAlternativeDecisionStrategy")]
    [InlineData("defaultdecisionstrategy", "DefaultDecisionStrategy")]
    [InlineData("MOCKALTERNATIVEDECISIONSTRATEGY", "MockAlternativeDecisionStrategy")]
    public void ResolveActive_Configured_ReturnsThatStrategy_Theory(string configured, string expectedName)
    {
        var registry = Build(configured, CreateDefault(), CreateMock());

        var active = registry.ResolveActive();

        active.Strategy.StrategyName.Should().Be(expectedName);
        active.Strategy.StrategyVersion.Should().Be("2.0.0");
        active.OptionsFingerprint.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Constructor_DuplicateName_Throws()
    {
        var act = () => Build("DefaultDecisionStrategy", CreateDefault(), CreateDefault());

        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultDecisionStrategy*");
    }

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("Foo")]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_UnknownActive_ThrowsListingAvailable(string active)
    {
        var act = () => Build(active, CreateDefault(), CreateMock());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultDecisionStrategy*")
            .WithMessage("*MockAlternativeDecisionStrategy*")
            .WithMessage("*Tadss:Strategy*");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void ResolveByName_Unknown_ThrowsKeyNotFound()
    {
        var registry = Build("DefaultDecisionStrategy", CreateDefault(), CreateMock());

        var act = () => registry.ResolveByName("Foo");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*Foo*");
    }

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("MockAlternativeDecisionStrategy")]
    [InlineData("mockalternativedecisionstrategy")]
    public void ResolveByName_KnownNameAnyCase_ReturnsThatStrategy(string name)
    {
        var registry = Build("DefaultDecisionStrategy", CreateDefault(), CreateMock());

        registry.ResolveByName(name).Strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void All_ListsEveryRegisteredStrategy()
    {
        var registry = Build("DefaultDecisionStrategy", CreateDefault(), CreateMock());

        registry.All.Select(registered => registered.Strategy.StrategyName)
            .Should().BeEquivalentTo("DefaultDecisionStrategy", "MockAlternativeDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Constructor_SingleStrategy_IsAccepted()
    {
        var registry = Build("DefaultDecisionStrategy", CreateDefault());

        registry.All.Should().ContainSingle();
        registry.ResolveActive().Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
    }
}
