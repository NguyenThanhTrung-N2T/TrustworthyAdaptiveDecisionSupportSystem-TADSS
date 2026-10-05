using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TADSS.Application.Common;
using TADSS.Application.Routing;
using TADSS.Infrastructure.Configuration;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using static TADSS.UnitTests.Application.Routing.StrategyRouterTests;

namespace TADSS.UnitTests.Application.Routing;

/// <summary>
/// UT-04 (A-02): <see cref="StrategyRouter"/> theo dõi cấu hình qua port <c>IRoutingRulesSource</c> (không qua <c>IOptionsMonitor</c>); ở đây dùng adapter thật
/// <see cref="ConfigurationRoutingRulesSource"/> trên <see cref="IConfiguration"/>. Mỗi lần cấu hình đổi:
/// đọc bản mới (kèm lỗi binder nuốt) → chạy validator → kiểm tên strategy → biên dịch → thay nguyên khối. Bất kỳ bước nào lỗi (validator, binder ném lỗi enum/Guid,
/// tên strategy lạ) thì giữ bảng cũ, log Error và tăng <c>tadss.config.reload_failures{section="Tadss:Routing"}</c>; callback không bao giờ ném.
/// Cấu hình là <see cref="IConfigurationRoot"/> trong bộ nhớ, đổi dữ liệu rồi gọi <see cref="IConfigurationRoot.Reload"/> (callback chạy đồng bộ) nên test tất định.
/// </summary>
public class StrategyRouterReloadTests
{
    private const string Section = "Tadss:Routing";

    /// <summary>Cấu hình ban đầu: rule <c>peak</c> (cao điểm → MockAlternative), mặc định Default.</summary>
    private static readonly Dictionary<string, string?> PeakRule = new()
    {
        ["Tadss:Routing:DefaultStrategy"] = Default,
        ["Tadss:Routing:Rules:0:Name"] = "peak",
        ["Tadss:Routing:Rules:0:Strategy"] = Mock,
        ["Tadss:Routing:Rules:0:When:PeakHour"] = "true",
    };

    /// <summary>Cấu hình thay thế hợp lệ: rule <c>gold</c> (hạng GOLD → MockAlternative), mặc định Default.</summary>
    private static readonly Dictionary<string, string?> GoldRule = new()
    {
        ["Tadss:Routing:DefaultStrategy"] = Default,
        ["Tadss:Routing:Rules:0:Name"] = "gold",
        ["Tadss:Routing:Rules:0:Strategy"] = Mock,
        ["Tadss:Routing:Rules:0:When:Tiers:0"] = "GOLD",
    };

    /// <summary>Các cấu hình sai khi nạp lại (đều là bản sửa của <see cref="PeakRule"/>).</summary>
    private static Dictionary<string, string?> Invalid(string scenario) => scenario switch
    {
        "rule-without-condition" => new()
        {
            ["Tadss:Routing:DefaultStrategy"] = Default,
            ["Tadss:Routing:Rules:0:Name"] = "catch-all",
            ["Tadss:Routing:Rules:0:Strategy"] = Default,
        },
        "unknown-strategy" => With(PeakRule, "Tadss:Routing:Rules:0:Strategy", "Foo"),
        "invalid-enum" => With(PeakRule, "Tadss:Routing:Rules:0:When:Channels:0", "PHONE"),
        "invalid-guid" => With(PeakRule, "Tadss:Routing:Rules:0:When:BranchIds:0", "not-a-guid"),
        "invalid-bool" => With(PeakRule, "Tadss:Routing:Rules:0:When:PeakHour", "maybe"),
        "scalar-channels" => With(PeakRule, "Tadss:Routing:Rules:0:When:Channels", "ONLINE"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Kịch bản không tồn tại.")
    };

    private static Dictionary<string, string?> With(Dictionary<string, string?> source, string key, string value) =>
        new(source) { [key] = value };

    /// <summary>Bộ dựng chung: cấu hình có thể thay toàn bộ, router đọc lần đầu và theo dõi chính cấu hình đó qua adapter thật, bộ ghi metric theo đúng instance meter.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly ReplaceableConfigurationProvider _provider;

        public Harness(Dictionary<string, string?> initial)
        {
            _provider = new ReplaceableConfigurationProvider(initial);
            Configuration = new ConfigurationBuilder().Add(new ReplaceableConfigurationSource(_provider)).Build();
            Recorder = new MetricRecorder(Metrics.Meter);
            Router = CreateRouter(new ConfigurationRoutingRulesSource(Configuration), Metrics, Logger);
        }

        public IConfigurationRoot Configuration { get; }

        public TadssMetrics Metrics { get; } = new();

        public MetricRecorder Recorder { get; }

        public CapturingLogger<StrategyRouter> Logger { get; } = new();

        public StrategyRouter Router { get; }

        public long ReloadFailures => Recorder.Sum(TadssMetrics.ConfigReloadFailures, ("section", Section));

        /// <summary>Thay toàn bộ dữ liệu cấu hình rồi <c>Reload()</c> (change token bắn đồng bộ).</summary>
        public void ReloadWith(Dictionary<string, string?> data)
        {
            _provider.Replace(data);
            Configuration.Reload();
        }

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> EntriesAt(LogLevel level) =>
            Logger.Entries.Where(entry => entry.Level == level).ToList();

        public void Dispose()
        {
            Router.Dispose();
            Recorder.Dispose();
            Metrics.Dispose();
        }
    }

    /// <summary>Đổi rule rồi nạp lại: <c>Route</c> dùng bảng mới ngay, không có lần nạp lại thất bại nào, có log Information về bảng mới.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Reload_ValidChange_SwapsTable()
    {
        using var harness = new Harness(PeakRule);
        var silverAtPeak = Reservation(tier: Shared.DecisionContractSDK.Enums.CustomerTier.SILVER);
        var goldOffPeak = Reservation(FridayOffPeak);
        harness.Router.Route(silverAtPeak).RuleName.Should().Be("peak");

        harness.ReloadWith(GoldRule);

        harness.Router.Route(silverAtPeak).Should().Match<StrategyRoute>(route => route.RuleName == null && route.Strategy.Strategy.StrategyName == Default);
        harness.Router.Route(goldOffPeak).Should().Match<StrategyRoute>(route => route.RuleName == "gold" && route.Strategy.Strategy.StrategyName == Mock);
        harness.ReloadFailures.Should().Be(0);
        harness.EntriesAt(LogLevel.Error).Should().BeEmpty();
        harness.EntriesAt(LogLevel.Information).Should().Contain(entry => entry.Message.Contains("gold", StringComparison.Ordinal));
    }

    /// <summary>
    /// Nạp lại với cấu hình sai (rule không điều kiện, tên strategy lạ, enum sai <c>PHONE</c>, Guid sai, bool sai): không exception nào lọt ra khỏi <c>Reload()</c>,
    /// <c>Route</c> giữ kết quả cũ, mỗi lần sai tăng reload_failures đúng 1 và có một dòng log Error nêu khóa sai. Danh sách khai báo bằng một giá trị đơn thay vì mảng (<c>Channels: "ONLINE"</c>) cũng bị binder bỏ qua và phải bị báo lỗi. Giá trị enum/Guid sai bị binder của .NET
    /// âm thầm bỏ khỏi danh sách (rule sẽ rộng hơn ý định) nên phải bị phát hiện riêng; log chỉ nêu đường dẫn khóa, không in giá trị cấu hình.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("rule-without-condition", "Tadss:Routing:Rules:0:When", null)]
    [InlineData("unknown-strategy", "Tadss:Routing:Rules:0:Strategy", null)]
    [InlineData("invalid-enum", "Tadss:Routing:Rules:0:When:Channels:0", "PHONE")]
    [InlineData("invalid-guid", "Tadss:Routing:Rules:0:When:BranchIds:0", "not-a-guid")]
    [InlineData("invalid-bool", "Tadss:Routing:Rules:0:When:PeakHour", "maybe")]
    [InlineData("scalar-channels", "Tadss:Routing:Rules:0:When:Channels", "ONLINE")]
    public void Reload_InvalidChange_KeepsPreviousTableAndCountsFailure_Theory(string scenario, string expectedKey, string? hiddenValue)
    {
        using var harness = new Harness(PeakRule);
        var peak = Reservation();
        var before = harness.Router.Route(peak);

        var first = () => harness.ReloadWith(Invalid(scenario));
        first.Should().NotThrow();
        harness.Router.Route(peak).Should().BeSameAs(before, "bảng cũ phải được giữ nguyên khi nạp lại thất bại");
        harness.ReloadFailures.Should().Be(1);
        var error = harness.EntriesAt(LogLevel.Error).Should().ContainSingle().Subject;
        error.Message.Should().Contain(expectedKey);
        if (hiddenValue is not null)
        {
            error.Message.Should().NotContain(hiddenValue, "log lỗi nạp lại không in giá trị cấu hình");
        }

        var second = () => harness.ReloadWith(Invalid(scenario));
        second.Should().NotThrow();
        harness.Router.Route(peak).Should().BeSameAs(before);
        harness.ReloadFailures.Should().Be(2, "mỗi lần nạp lại thất bại tăng đúng 1");
        harness.EntriesAt(LogLevel.Error).Should().HaveCount(2);
    }

    /// <summary>Lần nạp lại sai rồi sửa đúng: bảng mới được dùng, chỉ lần sai được đếm.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Reload_ThenValidAgain_Recovers()
    {
        using var harness = new Harness(PeakRule);
        var goldOffPeak = Reservation(FridayOffPeak);

        harness.ReloadWith(Invalid("unknown-strategy"));
        harness.Router.Route(goldOffPeak).RuleName.Should().BeNull("bảng cũ (peak) không khớp giờ thấp điểm");

        harness.ReloadWith(GoldRule);

        harness.Router.Route(goldOffPeak).Should().Match<StrategyRoute>(route => route.RuleName == "gold" && route.Strategy.Strategy.StrategyName == Mock);
        harness.ReloadFailures.Should().Be(1);
    }

    /// <summary>Cấu hình đổi ở section khác (bảng định tuyến không đổi): chỉ log Debug, không log Information, không đếm lỗi.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Reload_UnchangedTable_LogsDebugOnly()
    {
        using var harness = new Harness(PeakRule);
        var informationBefore = harness.EntriesAt(LogLevel.Information).Count;

        harness.ReloadWith(With(PeakRule, "Tadss:Approval:WindowMinutes", "45"));

        harness.EntriesAt(LogLevel.Information).Should().HaveCount(informationBefore);
        harness.EntriesAt(LogLevel.Debug).Should().ContainSingle();
        harness.ReloadFailures.Should().Be(0);
    }

    /// <summary>Sau <c>Dispose</c> router hủy đăng ký change token: cấu hình đổi không còn ảnh hưởng.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Dispose_StopsWatchingConfiguration()
    {
        using var harness = new Harness(PeakRule);
        var goldOffPeak = Reservation(FridayOffPeak);

        harness.Router.Dispose();
        harness.ReloadWith(GoldRule);

        harness.Router.Route(goldOffPeak).RuleName.Should().BeNull();
        harness.ReloadFailures.Should().Be(0);
    }

    /// <summary>
    /// <c>Route</c> chạy song song với nhiều lần nạp lại xen kẽ hai bảng: mọi kết quả đều thuộc trọn một trong hai bảng (không bao giờ thấy bảng dở dang)
    /// và không exception nào.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task Route_ConcurrentWithReload_AlwaysSeesWholeTable()
    {
        using var harness = new Harness(PeakRule);
        var goldAtPeak = Reservation();
        using var stop = new CancellationTokenSource();

        var routing = Task.Run(() =>
        {
            var observed = new HashSet<string?>();
            while (!stop.IsCancellationRequested)
            {
                var route = harness.Router.Route(goldAtPeak);
                route.Strategy.Strategy.StrategyName.Should().Be(Mock, "cả hai bảng đều đưa khách GOLD giờ cao điểm sang MockAlternative");
                observed.Add(route.RuleName);
            }

            return observed;
        });

        for (var round = 0; round < 200; round++)
        {
            harness.ReloadWith(round % 2 == 0 ? GoldRule : PeakRule);
        }

        stop.Cancel();
        (await routing).Should().BeSubsetOf(["peak", "gold"]);
        harness.ReloadFailures.Should().Be(0);
    }

    /// <summary>Nguồn cấu hình trong bộ nhớ có thể thay toàn bộ dữ liệu (kể cả xóa khóa) trước khi <c>Reload()</c>.</summary>
    private sealed class ReplaceableConfigurationSource(ReplaceableConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private sealed class ReplaceableConfigurationProvider : ConfigurationProvider
    {
        public ReplaceableConfigurationProvider(Dictionary<string, string?> initial) => Replace(initial);

        public void Replace(Dictionary<string, string?> data) =>
            Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
    }
}
