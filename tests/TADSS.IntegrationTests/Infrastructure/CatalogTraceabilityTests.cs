using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// UT-14 (O-22): mỗi test của <c>TADSS.IntegrationTests</c> gắn ít nhất một <c>CatalogId</c> có trong danh mục methodology; khi có biến <c>TADSS_CATALOG_INDEX_DIR</c> thì ghi
/// chỉ mục <c>{test → [mã]}</c> của assembly này. Dùng chung <see cref="CatalogIndex"/> với project unit test (compile-link). Chỉ đọc mã/file, không cần Docker.
/// </summary>
public class CatalogTraceabilityTests
{
    /// <summary>0 test thiếu mã và 0 mã lạ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void EveryTest_HasKnownCatalogId()
    {
        var assembly = typeof(CatalogTraceabilityTests).Assembly;
        var known = CatalogIndex.KnownIds();
        var index = CatalogIndex.Build(assembly);
        CatalogIndex.ExportIfRequested(assembly);

        index.Should().NotBeEmpty();
        index.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).Should().BeEmpty("mọi test phải có CatalogId");
        index.SelectMany(pair => pair.Value.Select(id => (Test: pair.Key, Id: id))).Where(entry => !known.Contains(entry.Id)).Select(entry => $"{entry.Test} → {entry.Id}")
            .Should().BeEmpty("mọi mã phải có trong bảng danh mục của methodology");
    }
}
