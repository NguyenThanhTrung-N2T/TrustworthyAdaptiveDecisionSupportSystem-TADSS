using System.Reflection;
using System.Text;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// Architectural Quality Gate: Verifies that the public API surface of Shared.DecisionContractSDK
/// strictly matches the committed baseline (v2.0.0-draft, chờ đóng băng M5) without any unauthorized modifications,
/// additions, or breaking deletions.
/// </summary>
public class PublicApiSurfaceTests
{
    [Fact]
    [Trait("CatalogId", "CT-02")]
    public void SharedDecisionContractSdk_PublicApiSurface_StrictlyMatchesFrozenBaseline()
    {
        // 1. Generate current Public API surface via reflection
        var sdkAssembly = typeof(Reservation).Assembly;
        var actualSurface = GeneratePublicApiSurface(sdkAssembly);

        // 2. Locate committed baseline artifact
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TADSS.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("Solution root directory containing TADSS.slnx must be found");

        var baselinePath = Path.Combine(dir!.FullName, "docs", "contracts", "baseline_public_api.txt");
        File.Exists(baselinePath).Should().BeTrue(
            $"Baseline public API file must exist at '{baselinePath}'. If this is a planned contract evolution, update the baseline explicitly.");

        var expectedSurface = File.ReadAllText(baselinePath, Encoding.UTF8);

        // 3. Normalize line endings (\r\n vs \n) and whitespace for cross-platform deterministic comparison
        var normalizedActual = NormalizeText(actualSurface);
        var normalizedExpected = NormalizeText(expectedSurface);

        // 4. Strict assertion against committed baseline (VERIFICATION ONLY - NEVER OVERWRITES IN TEST)
        //    Khi lệch: ghi bề mặt thực tế ra TestResults/ để người phát triển so sánh và cập nhật baseline có chủ đích.
        var testResultsDirectory = Path.GetFullPath(Path.Combine(baselinePath, "..", "..", "..", "TestResults"));
        var actualSnapshotPath = Path.Combine(testResultsDirectory, "baseline_public_api.actual.txt");
        if (normalizedActual != normalizedExpected)
        {
            Directory.CreateDirectory(testResultsDirectory);
            File.WriteAllText(actualSnapshotPath, actualSurface, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        normalizedActual.Should().Be(
            normalizedExpected,
            "the public API surface of Shared.DecisionContractSDK must strictly match the committed baseline v2.0.0-draft. " +
            "Any change indicates a breaking change or unauthorized contract modification. " +
            $"Actual surface written to '{actualSnapshotPath}' for comparison.");
    }

    [Fact]
    [Trait("CatalogId", "CT-02")]
    public void SharedDecisionContractSdk_BreakingChangeSimulation_DetectsMismatch()
    {
        var sdkAssembly = typeof(Reservation).Assembly;
        var actualSurface = GeneratePublicApiSurface(sdkAssembly);
        var normalizedActual = NormalizeText(actualSurface);

        // Simulate an unauthorized mutation (e.g., removing a class or changing a method signature)
        var simulatedTamperedBaseline = normalizedActual.Replace("Shared.DecisionContractSDK.Domain.Reservation", "Shared.DecisionContractSDK.Domain.ModifiedReservation");

        var act = () => normalizedActual.Should().Be(simulatedTamperedBaseline);
        act.Should().Throw<Exception>("any divergence between actual API surface and baseline must fail the gate");
    }

    private static string NormalizeText(string text)
    {
        return string.Join("\n", text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .Select(line => line.TrimEnd()))
            .Trim();
    }

    public static string GeneratePublicApiSurface(Assembly assembly)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Shared.DecisionContractSDK Public API Surface Snapshot v2.0.0-draft");
        sb.AppendLine($"# Assembly: {assembly.GetName().Name}, Version={assembly.GetName().Version}");
        sb.AppendLine("# STATUS: DRAFT v2.0.0 BASELINE (chờ đóng băng M5)");
        sb.AppendLine("--------------------------------------------------");

        var exportedTypes = assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        foreach (var t in exportedTypes)
        {
            sb.AppendLine();
            var kind = t.IsEnum ? "enum" : (t.IsInterface ? "interface" : (t.IsValueType ? "struct" : "class"));
            var baseType = "";
            if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum))
            {
                baseType = $" : {t.BaseType.Name}";
            }

            var interfaces = t.GetInterfaces().Select(i => i.Name).OrderBy(n => n).ToList();
            if (interfaces.Count > 0)
            {
                baseType = string.IsNullOrEmpty(baseType) ? $" : {string.Join(", ", interfaces)}" : $"{baseType}, {string.Join(", ", interfaces)}";
            }

            sb.AppendLine($"[{kind}] {t.FullName}{baseType}");

            if (t.IsEnum)
            {
                var names = Enum.GetNames(t);
                foreach (var name in names)
                {
                    var val = Convert.ToInt64(Enum.Parse(t, name));
                    sb.AppendLine($"    {name} = {val}");
                }
            }
            else
            {
                var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .OrderBy(c => c.GetParameters().Length)
                    .ThenBy(c => string.Join(",", c.GetParameters().Select(p => p.Name)));
                foreach (var c in ctors)
                {
                    var paramList = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    sb.AppendLine($"    .ctor({paramList})");
                }

                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .OrderBy(p => p.Name, StringComparer.Ordinal);
                foreach (var p in props)
                {
                    var accessors = "";
                    if (p.CanRead) accessors += "get; ";
                    if (p.CanWrite) accessors += "set; ";
                    sb.AppendLine($"    {p.PropertyType.Name} {p.Name} {{ {accessors}}}");
                }

                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ThenBy(m => m.GetParameters().Length);
                foreach (var m in methods)
                {
                    var paramList = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    sb.AppendLine($"    {m.ReturnType.Name} {m.Name}({paramList})");
                }
            }
        }

        return sb.ToString();
    }
}
