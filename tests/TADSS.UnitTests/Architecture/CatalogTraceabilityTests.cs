using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-14 (O-22, D-10): mỗi test của <c>TADSS.UnitTests</c> gắn ít nhất một <c>CatalogId</c> (trait ở phương thức hoặc lớp) và mọi mã có trong bảng danh mục của methodology
/// (cột ID của Mục 4); chỉ mục <c>{test → [mã]}</c> được xuất khi đặt <c>TADSS_CATALOG_INDEX_DIR</c> để evaluator gom kết quả theo mã.
/// </summary>
public class CatalogTraceabilityTests
{
    /// <summary>0 test thiếu mã và 0 mã lạ (không có trong danh mục). Khi có biến <c>TADSS_CATALOG_INDEX_DIR</c> thì ghi thêm chỉ mục của assembly.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void EveryTest_HasKnownCatalogId()
    {
        var assembly = typeof(CatalogTraceabilityTests).Assembly;
        var known = CatalogIndex.KnownIds();
        var index = CatalogIndex.Build(assembly);
        CatalogIndex.ExportIfRequested(assembly);

        known.Should().NotBeEmpty("phải đọc được danh mục của methodology");
        index.Should().NotBeEmpty();
        index.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).Should().BeEmpty("mọi test phải có CatalogId");
        index.SelectMany(pair => pair.Value.Select(id => $"{pair.Key} → {id}")).Where(entry => !known.Contains(entry.Split(" → ")[1]))
            .Should().BeEmpty("mọi mã phải có trong bảng danh mục của methodology");
    }

    /// <summary>
    /// Bộ đọc danh mục phía C# phải nhận cùng tập mã với <c>tools/evidence/coverage_map.py</c>: mã một đoạn (<c>UT-14</c>, <c>DOC-4</c>, <c>CT-04</c>, <c>FE-10</c>, <c>E2E-13</c>)
    /// lẫn mã hai đoạn tiền tố (<c>BE-UT-01</c>, <c>BE-IT-20</c>).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void KnownIds_CatalogHasTwoSegmentPrefixIds_ReadsThemLikeCoverageMap()
    {
        var known = CatalogIndex.KnownIds();

        known.Should().Contain(["BE-UT-01", "BE-IT-20", "FE-10", "E2E-13", "UT-14", "DOC-4", "CT-04"]);
    }

    /// <summary>Đặt thư mục xuất: file <c>catalog-index.{AssemblyName}.json</c> có mặt, chứa mọi test của assembly với đúng mã của chúng; không đặt thì không xuất.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void CatalogIndex_ExportedWhenDirectoryGiven()
    {
        var assembly = typeof(CatalogTraceabilityTests).Assembly;
        var directory = Path.Combine(Path.GetTempPath(), "tadss-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            CatalogIndex.Export(assembly, null).Should().BeNull("không có thư mục thì không xuất");

            var path = CatalogIndex.Export(assembly, directory);

            path.Should().Be(Path.Combine(directory, "catalog-index.TADSS.UnitTests.json"));
            var exported = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path!))!;
            var built = CatalogIndex.Build(assembly);
            exported.Keys.Should().BeEquivalentTo(built.Keys);
            exported[$"{typeof(CatalogTraceabilityTests).FullName}.{nameof(EveryTest_HasKnownCatalogId)}"].Should().Equal("UT-14");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
