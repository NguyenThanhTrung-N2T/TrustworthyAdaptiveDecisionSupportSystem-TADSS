using System.Xml.Linq;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// SEC-7: chuỗi cung ứng tái lập được — mọi project trong <c>TADSS.slnx</c> (src và tests) phải có <c>packages.lock.json</c> cạnh csproj
/// và <c>Directory.Build.props</c> phải bật <c>RestorePackagesWithLockFile</c>, để restore ở chế độ locked-mode (CI, Dockerfile) có lock file làm chuẩn.
/// </summary>
public class SupplyChainTests
{
    /// <summary>Mọi project trong solution phải có <c>packages.lock.json</c> cạnh file csproj.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-7")]
    public void AllProjects_HaveLockFiles()
    {
        var solution = XDocument.Load(RepositoryPaths.Resolve("TADSS.slnx"));
        var projectPaths = solution.Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value)
            .OfType<string>()
            .Select(path => path.Replace('\\', '/'))
            .ToList();

        projectPaths.Should().NotBeEmpty("TADSS.slnx phải khai báo project để kiểm tra");

        var missing = projectPaths
            .Where(path => !File.Exists(RepositoryPaths.Resolve(Path.GetDirectoryName(path) ?? string.Empty, "packages.lock.json")))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty("mọi project trong TADSS.slnx phải có packages.lock.json cạnh csproj; thiếu:{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, missing));
    }

    /// <summary><c>Directory.Build.props</c> ở gốc repo phải bật <c>RestorePackagesWithLockFile</c> để mọi project sinh và kiểm lock file.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-7")]
    public void DirectoryBuildProps_EnablesRestorePackagesWithLockFile()
    {
        var props = XDocument.Load(RepositoryPaths.Resolve("Directory.Build.props"));

        var value = props.Descendants("RestorePackagesWithLockFile").Select(element => element.Value.Trim()).SingleOrDefault();

        value.Should().Be("true", "RestorePackagesWithLockFile phải bật cho toàn repo");
    }
}
