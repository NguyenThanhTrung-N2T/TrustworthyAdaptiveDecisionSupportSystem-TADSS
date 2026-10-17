using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TADSS.Application.Strategies;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Application.Strategies;

/// <summary>
/// UT-04 (phần resolver): <see cref="StrategyRegistry"/> tra strategy v2 theo tên không phân biệt hoa thường (<c>TryResolve</c>, <c>ResolveByName</c>),
/// phát hiện trùng tên ngay lúc khởi tạo (fail-fast) và có thể dựng chỉ với một strategy (test double). Registry không còn chọn strategy kích hoạt:
/// việc chọn strategy theo request thuộc <c>StrategyRouter</c> (A-03), tên lạ trong <c>Tadss:Routing</c> được kiểm ở <c>StrategyRouterTests</c>.
/// </summary>
public class StrategyRegistryTests
{
    private static RegisteredStrategy CreateDefault() =>
        StrategyRegistrations.CreateDefault(new ConfigurationBuilder().Build(), DomainTestData.StandardCalendar());

    private static RegisteredStrategy CreateMock() =>
        StrategyRegistrations.CreateMockAlternative(new ConfigurationBuilder().Build(), DomainTestData.StandardCalendar());

    private static StrategyRegistry Build(params RegisteredStrategy[] strategies) =>
        new(strategies, NullLogger<StrategyRegistry>.Instance);

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("DefaultDecisionStrategy", "DefaultDecisionStrategy")]
    [InlineData("MockAlternativeDecisionStrategy", "MockAlternativeDecisionStrategy")]
    [InlineData("defaultdecisionstrategy", "DefaultDecisionStrategy")]
    [InlineData("MOCKALTERNATIVEDECISIONSTRATEGY", "MockAlternativeDecisionStrategy")]
    public void TryResolve_KnownNameAnyCase_ReturnsThatStrategy_Theory(string configured, string expectedName)
    {
        var registry = Build(CreateDefault(), CreateMock());

        registry.TryResolve(configured, out var resolved).Should().BeTrue();

        var strategy = ContractSamples.Required(resolved);
        strategy.Strategy.StrategyName.Should().Be(expectedName);
        strategy.Strategy.StrategyVersion.Should().Be(expectedName == "DefaultDecisionStrategy" ? "2.1.0" : "2.0.0");
        strategy.OptionsFingerprint.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Constructor_DuplicateName_Throws()
    {
        var act = () => Build(CreateDefault(), CreateDefault());

        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultDecisionStrategy*");
    }

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("Foo")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_UnknownOrBlankName_ReturnsFalse_Theory(string name)
    {
        var registry = Build(CreateDefault(), CreateMock());

        registry.TryResolve(name, out var resolved).Should().BeFalse();
        resolved.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void ResolveByName_Unknown_ThrowsKeyNotFound()
    {
        var registry = Build(CreateDefault(), CreateMock());

        var act = () => registry.ResolveByName("Foo");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*Foo*");
    }

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("MockAlternativeDecisionStrategy")]
    [InlineData("mockalternativedecisionstrategy")]
    public void ResolveByName_KnownNameAnyCase_ReturnsThatStrategy_Theory(string name)
    {
        var registry = Build(CreateDefault(), CreateMock());

        registry.ResolveByName(name).Strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void All_ListsEveryRegisteredStrategy()
    {
        var registry = Build(CreateDefault(), CreateMock());

        registry.All.Select(registered => registered.Strategy.StrategyName)
            .Should().BeEquivalentTo("DefaultDecisionStrategy", "MockAlternativeDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void All_RegisteredInReverseAlphabeticalOrder_PreservesRegistrationOrder()
    {
        var registry = Build(CreateMock(), CreateDefault());

        registry.All.Select(registered => registered.Strategy.StrategyName)
            .Should().Equal("MockAlternativeDecisionStrategy", "DefaultDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Constructor_SingleStrategy_IsAccepted()
    {
        var registry = Build(CreateDefault());

        registry.All.Should().ContainSingle();
        registry.ResolveByName("DefaultDecisionStrategy").Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
    }
}
