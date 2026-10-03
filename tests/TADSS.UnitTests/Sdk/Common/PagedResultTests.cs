using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Xunit;

namespace TADSS.UnitTests.Sdk.Common;

/// <summary>
/// UT-02: <see cref="PagedResult{T}"/> tính trường suy ra đúng và có hình dạng wire của INTEGRATION_SPEC §3.5.
/// </summary>
public class PagedResultTests
{
    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData(1, 20, 0, 0, false, false)]
    [InlineData(1, 20, 42, 3, true, false)]
    [InlineData(3, 20, 42, 3, false, true)]
    public void Create_ValidArguments_ComputesDerivedFields(
        int page, int pageSize, int totalItems, int expectedTotalPages, bool expectedHasNext, bool expectedHasPrev)
    {
        var result = PagedResult<string>.Create(["a"], page, pageSize, totalItems);

        result.TotalPages.Should().Be(expectedTotalPages);
        result.HasNext.Should().Be(expectedHasNext);
        result.HasPrev.Should().Be(expectedHasPrev);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData(0, 20, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 20, -1)]
    public void Create_InvalidArguments_ThrowsArgumentOutOfRange(int page, int pageSize, int totalItems)
    {
        var act = () => PagedResult<string>.Create([], page, pageSize, totalItems);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Serialize_TadssJson_WritesIntegrationSpecShape()
    {
        var json = JsonSerializer.Serialize(PagedResult<string>.Create(["a"], 1, 20, 1), TadssJson.Options);
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("JSON rỗng.");

        root.Select(property => property.Key).Should().BeEquivalentTo(
            "items", "page", "pageSize", "totalItems", "totalPages", "hasNext", "hasPrev");
    }
}
