using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// SEC-6 / SEC-7: khóa cấu hình container cứng bằng cách đọc thẳng <c>Dockerfile</c> và <c>.dockerignore</c> (không cần Docker):
/// stage cuối chạy user non-root trên image ASP.NET, không đưa .env/khóa riêng và appsettings.Development.json vào build context (từ Epic 6C, P6-33: image đẩy lên GHCR
/// nên không được chứa bí mật; khi chạy local, compose mount chỉ đọc file này vào container),
/// restore ở chế độ locked-mode với đủ <c>Directory.Build.props</c> và <c>packages.lock.json</c>.
/// Kiểm <c>Config.User</c> của image thật nằm ở bước CI <c>docker inspect</c>.
/// </summary>
public class ContainerHardeningTests
{
    private const string AspNetImagePrefix = "mcr.microsoft.com/dotnet/aspnet:";

    /// <summary>Các mẫu file bí mật bắt buộc có trong <c>.dockerignore</c> (gồm appsettings.Development.json từ Epic 6C, P6-33).</summary>
    public static TheoryData<string> SecretPatterns => new()
    {
        ".env",
        ".env.*",
        "**/appsettings.*.local.json",
        "**/appsettings.Development.json",
        "**/*.pfx"
    };

    /// <summary>Stage cuối dùng image ASP.NET runtime, và có <c>USER</c> non-root nằm sau <c>apk add</c> (cần root) và trước <c>ENTRYPOINT</c>.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-6")]
    public void Dockerfile_FinalStage_RunsAsNonRootOnAspNetImage()
    {
        var instructions = ReadDockerfileInstructions();
        var finalFromIndex = instructions.FindLastIndex(line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
        finalFromIndex.Should().BeGreaterThanOrEqualTo(0, "Dockerfile phải có ít nhất một lệnh FROM");

        var finalStage = instructions.Skip(finalFromIndex).ToList();
        finalStage[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]
            .Should().StartWith(AspNetImagePrefix, "stage cuối phải chạy trên image ASP.NET runtime (không chứa SDK)");

        var userIndex = finalStage.FindLastIndex(line => line.StartsWith("USER ", StringComparison.OrdinalIgnoreCase));
        var entrypointIndex = finalStage.FindIndex(line => line.StartsWith("ENTRYPOINT ", StringComparison.OrdinalIgnoreCase));
        var lastApkIndex = finalStage.FindLastIndex(line => line.Contains("apk add", StringComparison.Ordinal));

        userIndex.Should().BeGreaterThan(0, "stage cuối phải có lệnh USER");
        entrypointIndex.Should().BeGreaterThan(userIndex, "USER phải đứng trước ENTRYPOINT");
        userIndex.Should().BeGreaterThan(lastApkIndex, "USER phải đứng sau apk add vì apk cần quyền root");

        var user = finalStage[userIndex]["USER ".Length..].Trim();
        IsNonRoot(user).Should().BeTrue($"USER '{user}' phải là $APP_UID hoặc UID khác 0, không phải root");
    }

    /// <summary><c>.dockerignore</c> phải chứa đủ mẫu file bí mật để chúng không lọt vào build context.</summary>
    [Theory]
    [Trait("CatalogId", "SEC-6")]
    [MemberData(nameof(SecretPatterns))]
    public void DockerIgnore_ExcludesSecretFiles_Theory(string pattern)
    {
        var entries = File.ReadAllLines(RepositoryPaths.Resolve(".dockerignore"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        entries.Should().Contain(pattern, $".dockerignore phải loại '{pattern}' khỏi build context");
    }

    /// <summary>
    /// Dockerfile restore bằng <c>--locked-mode</c>; trước đó phải copy <c>Directory.Build.props</c> (để bật lock file trong container)
    /// và <c>packages.lock.json</c> của từng project cùng csproj.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-7")]
    public void Dockerfile_Restore_UsesLockedModeWithLockFilesCopiedFirst()
    {
        var instructions = ReadDockerfileInstructions();

        var restoreIndex = instructions.FindIndex(line => line.StartsWith("RUN dotnet restore", StringComparison.Ordinal));
        restoreIndex.Should().BeGreaterThanOrEqualTo(0, "Dockerfile phải có lệnh dotnet restore");
        instructions[restoreIndex].Should().Contain("--locked-mode", "restore trong container phải ở chế độ locked-mode");

        var beforeRestore = instructions.Take(restoreIndex).Where(line => line.StartsWith("COPY ", StringComparison.Ordinal)).ToList();

        beforeRestore.Should().Contain(line => line.Contains("Directory.Build.props", StringComparison.Ordinal),
            "thiếu Directory.Build.props thì RestorePackagesWithLockFile không áp dụng trong container");

        var projectCopies = beforeRestore.Where(line => line.Contains(".csproj\"", StringComparison.Ordinal)).ToList();
        projectCopies.Should().NotBeEmpty("Dockerfile phải copy csproj trước khi restore");
        projectCopies.Should().OnlyContain(line => line.Contains("packages.lock.json", StringComparison.Ordinal),
            "mỗi lệnh COPY csproj phải kèm packages.lock.json của project đó");
    }

    /// <summary>Hợp lệ khi là biến <c>$APP_UID</c> hoặc UID/tên khác root và khác 0 (phần sau dấu <c>:</c> là group, bỏ qua).</summary>
    private static bool IsNonRoot(string user)
    {
        var name = user.Split(':')[0];
        return name.Length > 0
               && !string.Equals(name, "root", StringComparison.OrdinalIgnoreCase)
               && name != "0";
    }

    /// <summary>Đọc Dockerfile, bỏ dòng trống và comment, nối các dòng tiếp nối bằng <c>\</c> thành một lệnh.</summary>
    private static List<string> ReadDockerfileInstructions()
    {
        var instructions = new List<string>();
        var pending = string.Empty;

        foreach (var rawLine in File.ReadAllLines(RepositoryPaths.Resolve("Dockerfile")))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.EndsWith('\\'))
            {
                pending += line[..^1].Trim() + " ";
                continue;
            }

            instructions.Add((pending + line).Trim());
            pending = string.Empty;
        }

        return instructions;
    }
}
