using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Services;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using Xunit;

namespace TADSS.UnitTests.Services;

/// <summary>
/// Bộ kiểm thử xác thực hoạt động của StrategyResolver và các Invariants về phân giải chiến lược.
/// </summary>
public class StrategyResolverTests
{
    private static IReservationDecisionStrategy[] CreateStandardStrategies() =>
    [
        new DefaultDecisionStrategy(),
        new MockAlternativeDecisionStrategy()
    ];

    private static StrategyResolver CreateResolver(
        string activeStrategyName,
        IEnumerable<IReservationDecisionStrategy>? customStrategies = null)
    {
        var strategies = customStrategies ?? CreateStandardStrategies();
        var options = Options.Create(new StrategyOptions { ActiveStrategyName = activeStrategyName });
        var logger = NullLogger<StrategyResolver>.Instance;

        return new StrategyResolver(strategies, options, logger);
    }

    [Fact]
    public void ResolveActive_WhenConfiguredDefault_ReturnsDefaultDecisionStrategy()
    {
        // Arrange
        var resolver = CreateResolver("DefaultDecisionStrategy");

        // Act
        var strategy = resolver.ResolveActive();

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<DefaultDecisionStrategy>();
        strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
        strategy.StrategyVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void ResolveActive_WhenConfiguredMockAlternative_ReturnsMockAlternativeDecisionStrategy()
    {
        // Arrange
        var resolver = CreateResolver("MockAlternativeDecisionStrategy");

        // Act
        var strategy = resolver.ResolveActive();

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<MockAlternativeDecisionStrategy>();
        strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        strategy.StrategyVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void ResolveActive_CaseInsensitiveLookup_ReturnsCorrectStrategy()
    {
        // Arrange (lowercase config name)
        var resolver = CreateResolver("defaultdecisionstrategy");

        // Act
        var strategy = resolver.ResolveActive();

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<DefaultDecisionStrategy>();
    }

    [Fact]
    public void Constructor_WhenDuplicateStrategyNameRegistered_ThrowsInvalidOperationException_FailFast()
    {
        // Arrange: 2 strategies with same StrategyName
        var duplicateStrategy1 = new DefaultDecisionStrategy();
        var duplicateStrategy2 = new DuplicateMockStrategy("DefaultDecisionStrategy");
        var strategies = new IReservationDecisionStrategy[] { duplicateStrategy1, duplicateStrategy2 };
        var options = Options.Create(new StrategyOptions { ActiveStrategyName = "DefaultDecisionStrategy" });
        var logger = NullLogger<StrategyResolver>.Instance;

        // Act
        var act = () => new StrategyResolver(strategies, options, logger);

        // Assert: Invariant - Strategy names are unique identities, must fail fast
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate StrategyName*DefaultDecisionStrategy*");
    }

    [Fact]
    public void Constructor_WhenConfiguredActiveStrategyNotFound_ThrowsInvalidOperationException_StartupValidation()
    {
        // Arrange: Configure non-existent strategy name
        var strategies = CreateStandardStrategies();
        var options = Options.Create(new StrategyOptions { ActiveStrategyName = "NonExistentStrategy" });
        var logger = NullLogger<StrategyResolver>.Instance;

        // Act
        var act = () => new StrategyResolver(strategies, options, logger);

        // Assert: Invariant - Invalid strategy configuration MUST prevent host startup
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Configured ActiveStrategyName 'NonExistentStrategy' not found in registry*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenActiveStrategyNameIsEmpty_ThrowsInvalidOperationException(string? invalidName)
    {
        // Arrange
        var strategies = CreateStandardStrategies();
        var options = Options.Create(new StrategyOptions { ActiveStrategyName = invalidName! });
        var logger = NullLogger<StrategyResolver>.Instance;

        // Act
        var act = () => new StrategyResolver(strategies, options, logger);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveByName_WhenStrategyExists_ReturnsCorrectInstance()
    {
        // Arrange
        var resolver = CreateResolver("DefaultDecisionStrategy");

        // Act
        var strategy = resolver.ResolveByName("MockAlternativeDecisionStrategy");

        // Assert
        strategy.Should().NotBeNull();
        strategy.Should().BeOfType<MockAlternativeDecisionStrategy>();
        strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
    }

    [Fact]
    public void ResolveByName_WhenStrategyNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var resolver = CreateResolver("DefaultDecisionStrategy");

        // Act
        var act = () => resolver.ResolveByName("UnknownStrategy");

        // Assert: Invariant - Explicit lookup for non-existent strategy throws explicit KeyNotFoundException
        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*UnknownStrategy*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveByName_WhenStrategyNameIsNullOrWhiteSpace_ThrowsArgumentException(string? invalidName)
    {
        // Arrange
        var resolver = CreateResolver("DefaultDecisionStrategy");

        // Act
        var act = () => resolver.ResolveByName(invalidName!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ListRegistered_ReturnsAllRegisteredStrategiesWithCorrectMetadata()
    {
        // Arrange
        var resolver = CreateResolver("DefaultDecisionStrategy");

        // Act
        var registered = resolver.ListRegistered();

        // Assert
        registered.Should().HaveCount(2);
        registered.Should().Contain(s => s.Name == "DefaultDecisionStrategy" && s.Version == "1.0.0");
        registered.Should().Contain(s => s.Name == "MockAlternativeDecisionStrategy" && s.Version == "1.0.0");
    }

    // Helper mock class to test duplicate StrategyName fail-fast
    private sealed class DuplicateMockStrategy : IReservationDecisionStrategy
    {
        public string StrategyName { get; }
        public string StrategyVersion => "1.0.0";

        public DuplicateMockStrategy(string name)
        {
            StrategyName = name;
        }

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
