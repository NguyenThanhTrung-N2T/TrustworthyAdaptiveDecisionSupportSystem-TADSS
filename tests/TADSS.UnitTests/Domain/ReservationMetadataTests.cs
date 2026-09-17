using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Xunit;

namespace TADSS.UnitTests.Domain;

public class ReservationMetadataTests
{
    [Fact]
    public void Constructor_WithNullAttributes_CreatesEmptyReadOnlyCollection()
    {
        var metadata = new ReservationMetadata();

        metadata.Attributes.Should().BeEmpty();
        metadata.Attributes.Should().BeAssignableTo<IReadOnlyDictionary<string, string>>();
        metadata.Get("missing").Should().BeNull();
    }

    [Fact]
    public void Constructor_DefensivelyCopiesInputDictionary()
    {
        var source = new Dictionary<string, string>
        {
            ["source"] = "MobileApp",
            ["tenant"] = "smashcourt"
        };

        var metadata = new ReservationMetadata(source);

        source["source"] = "Changed";
        source["new-key"] = "new-value";

        metadata.Get("source").Should().Be("MobileApp");
        metadata.Get("tenant").Should().Be("smashcourt");
        metadata.Get("new-key").Should().BeNull();
    }

    [Fact]
    public void Get_IsCaseSensitiveByDefault()
    {
        var metadata = new ReservationMetadata(
            new Dictionary<string, string> { ["Source"] = "MobileApp" });

        metadata.Get("Source").Should().Be("MobileApp");
        metadata.Get("source").Should().BeNull();
    }

    [Fact]
    public void TwoMetadataInstances_WithSameAttributes_HaveEquivalentContent()
    {
        var attributes = new Dictionary<string, string>
        {
            ["source"] = "MobileApp",
            ["tenant"] = "smashcourt"
        };

        var first = new ReservationMetadata(attributes);
        var second = new ReservationMetadata(attributes);

        first.Attributes.Should().BeEquivalentTo(second.Attributes);
    }
}
