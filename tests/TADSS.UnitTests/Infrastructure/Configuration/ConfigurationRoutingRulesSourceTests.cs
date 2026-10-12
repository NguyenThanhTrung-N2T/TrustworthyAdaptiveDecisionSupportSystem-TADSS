using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Shared.DecisionContractSDK.Enums;
using TADSS.Infrastructure.Configuration;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Configuration;

/// <summary>
/// UT-04 (A-02): adapter <see cref="ConfigurationRoutingRulesSource"/> của port <c>IRoutingRulesSource</c> — <c>Read</c> bind <c>Tadss:Routing</c> từ
/// <see cref="IConfiguration"/> và báo các lỗi mà binder của .NET nuốt (chỉ đường dẫn khóa, không giá trị), không ném; <c>Subscribe</c> gọi callback mỗi lần
/// cấu hình nạp lại và dừng sau khi dispose. Kịch bản đầy đủ của router (khởi động, nạp lại) nằm ở <c>StrategyRouterTests</c>/<c>StrategyRouterReloadTests</c>.
/// </summary>
public class ConfigurationRoutingRulesSourceTests
{
    private const string Mock = "MockAlternativeDecisionStrategy";

    private static readonly Dictionary<string, string?> PeakRule = new()
    {
        ["Tadss:Routing:DefaultStrategy"] = "DefaultDecisionStrategy",
        ["Tadss:Routing:Rules:0:Name"] = "peak",
        ["Tadss:Routing:Rules:0:Strategy"] = Mock,
        ["Tadss:Routing:Rules:0:When:PeakHour"] = "true",
        ["Tadss:Routing:Rules:0:When:Tiers:0"] = "GOLD",
    };

    private static IConfigurationRoot Build(Dictionary<string, string?> data) =>
        new ConfigurationBuilder().AddInMemoryCollection(data).Build();

    /// <summary>Cấu hình hợp lệ: bind đủ mặc định và rule, không thêm lỗi nào.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Read_ValidSection_BindsWithoutFailures()
    {
        var failures = new List<string>();

        var options = new ConfigurationRoutingRulesSource(Build(PeakRule)).Read(failures);

        failures.Should().BeEmpty();
        options.DefaultStrategy.Should().Be("DefaultDecisionStrategy");
        var rule = options.Rules.Should().ContainSingle().Subject;
        rule.Name.Should().Be("peak");
        rule.Strategy.Should().Be(Mock);
        rule.When.PeakHour.Should().BeTrue();
        rule.When.Tiers.Should().Equal(CustomerTier.GOLD);
    }

    /// <summary>
    /// Lỗi binder nuốt (phần tử danh sách sai kiểu hoặc rỗng, giá trị đơn sai kiểu làm mất cả rule): <c>Read</c> không ném, thêm đúng một lỗi nêu đường dẫn
    /// khóa và không in giá trị; lỗi có sẵn trong danh sách được giữ nguyên.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("Tadss:Routing:Rules:0:When:Channels:0", "PHONE")]
    [InlineData("Tadss:Routing:Rules:0:When:Tiers:0", "VIP")]
    [InlineData("Tadss:Routing:Rules:0:When:BranchIds:0", "not-a-guid")]
    [InlineData("Tadss:Routing:Rules:0:When:Channels:0", " ")]
    [InlineData("Tadss:Routing:Rules:0:When:PeakHour", "maybe")]
    public void Read_SwallowedBindingError_ReportsKeyWithoutValue_Theory(string key, string value)
    {
        var failures = new List<string> { "lỗi có sẵn" };
        var source = new ConfigurationRoutingRulesSource(Build(new Dictionary<string, string?>(PeakRule) { [key] = value }));

        var act = () => source.Read(failures);

        act.Should().NotThrow();
        failures.Should().HaveCount(2).And.StartWith("lỗi có sẵn");
        failures[1].Should().Contain(key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            failures[1].Should().NotContain(value, "lỗi cấu hình chỉ nêu đường dẫn khóa, không in giá trị");
        }
    }

    /// <summary>
    /// Danh sách <c>BranchIds</c>/<c>Channels</c>/<c>Tiers</c> khai báo bằng MỘT giá trị đơn (JSON <c>"Channels": "ONLINE"</c> hoặc biến môi trường
    /// <c>...When__Channels=ONLINE</c>) thay vì mảng: binder của .NET bỏ qua âm thầm nên danh sách rỗng và rule rộng hơn ý định. <c>Read</c> không ném, thêm đúng một lỗi
    /// nêu đường dẫn khóa của danh sách (không phải phần tử) và không in giá trị.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("Tadss:Routing:Rules:0:When:Channels", "ONLINE")]
    [InlineData("Tadss:Routing:Rules:0:When:Tiers", "GOLD")]
    [InlineData("Tadss:Routing:Rules:0:When:BranchIds", "3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    public void Read_ScalarInsteadOfList_ReportsKeyWithoutValue_Theory(string key, string value)
    {
        var failures = new List<string> { "lỗi có sẵn" };
        var source = new ConfigurationRoutingRulesSource(Build(new Dictionary<string, string?>(PeakRule) { [key] = value }));

        var act = () => source.Read(failures);

        act.Should().NotThrow();
        failures.Should().HaveCount(2).And.StartWith("lỗi có sẵn");
        failures[1].Should().Contain(key).And.Contain("danh sách");
        failures[1].Should().NotContain(value, "lỗi cấu hình chỉ nêu đường dẫn khóa, không in giá trị");
    }

    /// <summary><c>Read</c> luôn đọc bản hiện tại của cấu hình (không chụp lúc tạo adapter).</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Read_AfterConfigurationChange_ReturnsCurrentValues()
    {
        var configuration = Build(PeakRule);
        var source = new ConfigurationRoutingRulesSource(configuration);

        configuration["Tadss:Routing:DefaultStrategy"] = Mock;

        source.Read(new List<string>()).DefaultStrategy.Should().Be(Mock);
    }

    /// <summary><c>Subscribe</c>: mỗi lần cấu hình nạp lại gọi callback đúng một lần; sau khi dispose đăng ký thì không gọi nữa.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Subscribe_ConfigurationReload_InvokesCallbackUntilDisposed()
    {
        var configuration = Build(PeakRule);
        var calls = 0;
        var registration = new ConfigurationRoutingRulesSource(configuration).Subscribe(() => calls++);

        configuration.Reload();
        configuration.Reload();
        calls.Should().Be(2);

        registration.Dispose();
        configuration.Reload();
        calls.Should().Be(2, "đã hủy theo dõi");
    }
}
