using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TADSS.Infrastructure.Options;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-09 (A-07): <see cref="FaultInjectionStartupCheck"/> cảnh báo khi khởi động nhưng không bao giờ dừng ứng dụng —
/// cờ bật ngoài <c>Testing</c> (bị bỏ qua), tiêm lỗi đang chạy ở <c>Testing</c>, và <c>TargetStrategy</c> lạ; cờ tắt thì im lặng.
/// </summary>
public class FaultInjectionStartupCheckTests
{
    private static readonly string[] Registered = ["DefaultDecisionStrategy", "MockAlternativeDecisionStrategy"];

    private static IHostEnvironment Environment(string name) =>
        Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = name }).Environment;

    private static List<(LogLevel Level, string Message, Exception? Exception)> Run(string environment, FaultInjectionOptions options)
    {
        var logger = new CapturingLogger<FaultInjectionStartupCheckTests>();
        FaultInjectionStartupCheck.Log(logger, Environment(environment), options, Registered);
        return [.. logger.Entries];
    }

    /// <summary>Cờ tắt thì không log gì ở bất kỳ môi trường nào, kể cả khi <c>Profile</c> khác <c>None</c>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("Testing")]
    [InlineData("Production")]
    public void Log_Disabled_IsSilent(string environment)
    {
        Run(environment, new FaultInjectionOptions { Enabled = false, Profile = FaultInjectionProfile.Throw }).Should().BeEmpty();
    }

    /// <summary><c>Enabled = true</c> ngoài <c>Testing</c>: đúng một Warning nêu cờ bị bỏ qua và tên môi trường.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void Log_EnabledOutsideTesting_WarnsOnce_Theory(string environment)
    {
        var entries = Run(environment, new FaultInjectionOptions { Enabled = true, Profile = FaultInjectionProfile.Throw });

        var entry = entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("bị bỏ qua").And.Contain(environment);
    }

    /// <summary><c>Testing</c> với cấu hình hợp lệ: một Warning cho biết tiêm lỗi đang bật, không có cảnh báo tên lạ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Log_EnabledInTesting_WarnsInjectionActive()
    {
        var entries = Run("Testing", new FaultInjectionOptions { Enabled = true, Profile = FaultInjectionProfile.Delay, TargetStrategy = "defaultdecisionstrategy" });

        var entry = entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("đang BẬT").And.Contain("Delay");
    }

    /// <summary><c>Testing</c> với <c>TargetStrategy</c> không có trong registry: thêm Warning nêu tên lạ và các strategy hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Log_EnabledInTestingWithUnknownTarget_WarnsAboutTarget()
    {
        var entries = Run("Testing", new FaultInjectionOptions { Enabled = true, Profile = FaultInjectionProfile.Throw, TargetStrategy = "Foo" });

        entries.Should().HaveCount(2).And.OnlyContain(entry => entry.Level == LogLevel.Warning);
        entries[1].Message.Should().Contain("Foo").And.Contain("DefaultDecisionStrategy");
    }
}
