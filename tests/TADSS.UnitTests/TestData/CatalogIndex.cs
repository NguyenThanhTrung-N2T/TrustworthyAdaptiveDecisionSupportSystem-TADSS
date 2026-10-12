using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TADSS.UnitTests.TestData;

/// <summary>
/// Truy vết mã danh mục kiểm thử (O-22, D-10): đọc danh mục trong methodology (cột ID của các bảng ở Mục 4), quét reflection mọi phương thức <c>[Fact]</c>/<c>[Theory]</c>
/// lấy trait <c>CatalogId</c> (ở phương thức, lớp hoặc lớp cơ sở) và ghi chỉ mục <c>{tên đầy đủ phương thức → [mã]}</c> ra <c>catalog-index.{AssemblyName}.json</c>
/// khi có biến môi trường <see cref="IndexDirectoryVariable"/>; evaluator ghép chỉ mục với trx để gom kết quả theo mã mà không dựa vào việc logger trx có ghi trait hay không.
/// Public và được project IntegrationTests compile-link trực tiếp để dùng chung một nguồn.
/// </summary>
public static class CatalogIndex
{
    /// <summary>Tên biến môi trường chứa thư mục ghi chỉ mục.</summary>
    public const string IndexDirectoryVariable = "TADSS_CATALOG_INDEX_DIR";

    /// <summary>Tên trait chứa mã danh mục.</summary>
    public const string TraitName = "CatalogId";

    // Cùng hình dạng với _CODE_ROW trong tools/evidence/coverage_map.py: một hoặc hai đoạn tiền tố (UT-01, E2E-13, BE-UT-01), hậu tố chữ thường tuỳ chọn.
    private static readonly Regex IdCell = new(@"^\|\s*([A-Z][A-Z0-9]*(?:-[A-Z]{2})?-\d{1,2}[a-z]?)\s*\|", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Mọi mã trong bảng danh mục của methodology (hàng bảng bắt đầu bằng <c>| XX-nn |</c> hoặc <c>| BE-UT-nn |</c>).</summary>
    public static IReadOnlySet<string> KnownIds()
    {
        var path = RepositoryPaths.Resolve("docs", "research", "EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md");
        return IdCell.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Chỉ mục của <paramref name="assembly"/>: tên đầy đủ <c>Namespace.Class.Method</c> → các mã (rỗng nếu thiếu trait). Chỉ gồm phương thức test (có thuộc tính xunit <c>Fact</c>/<c>Theory</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Build(Assembly assembly)
    {
        var index = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes().Where(type => type.IsClass))
        {
            var classIds = IdsOf(HierarchyAttributes(type));
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!IsTestMethod(method))
                {
                    continue;
                }

                var ids = classIds.Concat(IdsOf(method.GetCustomAttributesData())).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
                index[$"{type.FullName}.{method.Name}"] = ids;
            }
        }

        return index;
    }

    /// <summary>
    /// Ghi chỉ mục của <paramref name="assembly"/> ra <c>catalog-index.{AssemblyName}.json</c> trong thư mục <paramref name="directory"/> (tạo nếu chưa có).
    /// Trả đường dẫn file, hoặc <c>null</c> khi <paramref name="directory"/> rỗng (không yêu cầu xuất).
    /// </summary>
    public static string? Export(Assembly assembly, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"catalog-index.{assembly.GetName().Name}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Build(assembly), new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    /// <summary>Xuất chỉ mục theo biến môi trường <see cref="IndexDirectoryVariable"/> (không làm gì khi biến không có giá trị).</summary>
    public static string? ExportIfRequested(Assembly assembly) => Export(assembly, Environment.GetEnvironmentVariable(IndexDirectoryVariable));

    private static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttributesData().Any(data => data.AttributeType.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute"
            || IsDerivedFrom(data.AttributeType, "Xunit.FactAttribute"));

    private static bool IsDerivedFrom(Type type, string baseFullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == baseFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<CustomAttributeData> HierarchyAttributes(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var data in current.GetCustomAttributesData())
            {
                yield return data;
            }
        }
    }

    private static IEnumerable<string> IdsOf(IEnumerable<CustomAttributeData> attributes) =>
        attributes
            .Where(data => data.AttributeType.FullName == "Xunit.TraitAttribute" && data.ConstructorArguments.Count == 2)
            .Where(data => data.ConstructorArguments[0].Value as string == TraitName)
            .Select(data => data.ConstructorArguments[1].Value as string)
            .OfType<string>();
}
