using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Configuration;
using TADSS.Application.Common;
using TADSS.Application.Options;
using TADSS.Application.Routing;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.Configuration;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Routing;

/// <summary>
/// UT-04 (A-01): <see cref="StrategyRouter"/> chọn strategy theo từng request — mọi điều kiện trong một rule là AND, rule đầu tiên khớp thắng,
/// không rule nào khớp thì dùng <c>DefaultStrategy</c>; danh sách rỗng là không ràng buộc; giờ cao điểm tính theo giờ chi nhánh qua
/// <see cref="BranchCalendar"/> dùng chung; tên strategy không phân biệt hoa thường và tên lạ làm router không dựng được (fail-fast).
/// </summary>
public class StrategyRouterTests
{
    internal const string Default = "DefaultDecisionStrategy";
    internal const string Mock = "MockAlternativeDecisionStrategy";

    private static readonly Guid SelectedBranch = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001");
    private static readonly Guid OtherBranch = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000002");

    /// <summary>Thứ Sáu 20/11/2026 10:00 giờ Việt Nam (ngoài giờ cao điểm).</summary>
    internal static readonly DateTimeOffset FridayOffPeak = new(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(7));

    /// <summary>Registry thật với hai strategy của runtime (Default, MockAlternative) dựng từ tham số mặc định.</summary>
    internal static IStrategyRegistry Registry() =>
        new StrategyRegistry(
            [
                StrategyRegistrations.CreateDefault(new ConfigurationBuilder().Build(), DomainTestData.StandardCalendar()),
                StrategyRegistrations.CreateMockAlternative(new ConfigurationBuilder().Build(), DomainTestData.StandardCalendar())
            ],
            NullLogger<StrategyRegistry>.Instance);

    /// <summary>Router với lịch cao điểm chuẩn, validator thật và nguồn cấu hình <paramref name="source"/>.</summary>
    internal static StrategyRouter CreateRouter(
        IRoutingRulesSource source,
        TadssMetrics? metrics = null,
        CapturingLogger<StrategyRouter>? logger = null) =>
        new(
            source,
            [new StrategyRoutingOptionsValidator()],
            Registry(),
            DomainTestData.StandardCalendar(),
            metrics ?? new TadssMetrics(),
            logger ?? new CapturingLogger<StrategyRouter>());

    /// <summary>Router đọc cố định <paramref name="options"/> (nguồn giả không bao giờ báo đổi).</summary>
    internal static StrategyRouter CreateRouter(StrategyRoutingOptions options) => CreateRouter(new FakeRoutingRulesSource(options));

    internal static RoutingRuleOptions Rule(string name, string strategy, Action<RoutingConditionOptions> when)
    {
        var condition = new RoutingConditionOptions();
        when(condition);
        return new RoutingRuleOptions { Name = name, Strategy = strategy, When = condition };
    }

    internal static StrategyRoutingOptions Routing(string defaultStrategy, params RoutingRuleOptions[] rules) =>
        new() { DefaultStrategy = defaultStrategy, Rules = [.. rules] };

    /// <summary>Reservation một item (mặc định thứ Sáu 18:00 giờ Việt Nam — cao điểm), khách GOLD, kênh ONLINE, chi nhánh <see cref="SelectedBranch"/>.</summary>
    internal static Reservation Reservation(
        DateTimeOffset? start = null,
        CustomerTier tier = CustomerTier.GOLD,
        bool guest = false,
        Channel channel = Channel.ONLINE,
        Guid? branchId = null) =>
        DomainTestData.CreateReservation(
            items: [DomainTestData.CreateItem(start ?? DomainTestData.FridayPeakStart)],
            customer: guest ? DomainTestData.CreateGuest() : DomainTestData.CreateMember(tier),
            channel: channel,
            branchId: branchId ?? SelectedBranch);

    /// <summary>Kịch bản của <see cref="Route_RuleTable_Theory"/>: cấu hình định tuyến và reservation cần định tuyến.</summary>
    private static (StrategyRoutingOptions Options, Reservation Reservation) Scenario(string scenario) => scenario switch
    {
        "peak-match" => (Routing(Default, Rule("peak", Mock, w => w.PeakHour = true)), Reservation()),
        "peak-no-match" => (Routing(Default, Rule("peak", Mock, w => w.PeakHour = true)), Reservation(FridayOffPeak)),
        "off-peak-match" => (Routing(Default, Rule("off-peak", Mock, w => w.PeakHour = false)), Reservation(FridayOffPeak)),
        "off-peak-no-match" => (Routing(Default, Rule("off-peak", Mock, w => w.PeakHour = false)), Reservation()),
        "branch-match" => (Routing(Default, Rule("branch", Mock, w => w.BranchIds = [OtherBranch, SelectedBranch])), Reservation()),
        "branch-no-match" => (Routing(Default, Rule("branch", Mock, w => w.BranchIds = [OtherBranch])), Reservation()),
        "channel-match" => (Routing(Default, Rule("walk-in", Mock, w => w.Channels = [Channel.WALK_IN])), Reservation(channel: Channel.WALK_IN)),
        "channel-no-match" => (Routing(Default, Rule("walk-in", Mock, w => w.Channels = [Channel.WALK_IN])), Reservation()),
        "tier-match" => (Routing(Default, Rule("vip", Mock, w => w.Tiers = [CustomerTier.PLATINUM, CustomerTier.GOLD])), Reservation()),
        "tier-no-match" => (Routing(Default, Rule("vip", Mock, w => w.Tiers = [CustomerTier.PLATINUM])), Reservation()),
        "guest-match" => (Routing(Default, Rule("guest", Mock, w => w.IsGuest = true)), Reservation(guest: true)),
        "guest-no-match" => (Routing(Default, Rule("guest", Mock, w => w.IsGuest = true)), Reservation()),
        "member-match" => (Routing(Default, Rule("member", Mock, w => w.IsGuest = false)), Reservation()),
        "and-both-match" => (Routing(Default, Rule("gold-peak", Mock, w => { w.PeakHour = true; w.Tiers = [CustomerTier.GOLD]; })), Reservation()),
        "and-one-fails" => (
            Routing(Default, Rule("gold-peak", Mock, w => { w.PeakHour = true; w.Tiers = [CustomerTier.GOLD]; })),
            Reservation(tier: CustomerTier.SILVER)),
        "first-match-wins" => (
            Routing(Default, Rule("first", Mock, w => w.Tiers = [CustomerTier.GOLD]), Rule("second", Default, w => w.PeakHour = true)),
            Reservation()),
        "second-when-first-fails" => (
            Routing(Mock, Rule("first", Mock, w => w.Tiers = [CustomerTier.DIAMOND]), Rule("second", Default, w => w.PeakHour = true)),
            Reservation()),
        "none-match-uses-default" => (
            Routing(Mock, Rule("walk-in", Default, w => w.Channels = [Channel.WALK_IN]), Rule("guest", Default, w => w.IsGuest = true)),
            Reservation()),
        "no-rules-uses-default" => (Routing(Mock), Reservation()),
        "empty-lists-unconstrained" => (
            Routing(Default, Rule("peak-any", Mock, w => { w.PeakHour = true; w.BranchIds = []; w.Channels = []; w.Tiers = []; })),
            Reservation(tier: CustomerTier.STANDARD, channel: Channel.WALK_IN, branchId: OtherBranch)),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Kịch bản không tồn tại.")
    };

    /// <summary>
    /// Bảng quy tắc: từng điều kiện riêng lẻ khớp/không khớp, AND hai điều kiện, hai rule cùng khớp (rule đầu thắng), không rule nào khớp,
    /// danh sách rỗng là không ràng buộc. <c>RuleName</c> là <c>null</c> khi dùng strategy mặc định.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("peak-match", Mock, "peak")]
    [InlineData("peak-no-match", Default, null)]
    [InlineData("off-peak-match", Mock, "off-peak")]
    [InlineData("off-peak-no-match", Default, null)]
    [InlineData("branch-match", Mock, "branch")]
    [InlineData("branch-no-match", Default, null)]
    [InlineData("channel-match", Mock, "walk-in")]
    [InlineData("channel-no-match", Default, null)]
    [InlineData("tier-match", Mock, "vip")]
    [InlineData("tier-no-match", Default, null)]
    [InlineData("guest-match", Mock, "guest")]
    [InlineData("guest-no-match", Default, null)]
    [InlineData("member-match", Mock, "member")]
    [InlineData("and-both-match", Mock, "gold-peak")]
    [InlineData("and-one-fails", Default, null)]
    [InlineData("first-match-wins", Mock, "first")]
    [InlineData("second-when-first-fails", Default, "second")]
    [InlineData("none-match-uses-default", Mock, null)]
    [InlineData("no-rules-uses-default", Mock, null)]
    [InlineData("empty-lists-unconstrained", Mock, "peak-any")]
    public void Route_RuleTable_Theory(string scenario, string expectedStrategy, string? expectedRule)
    {
        var (options, reservation) = Scenario(scenario);
        using var router = CreateRouter(options);

        var route = router.Route(reservation);

        route.Strategy.Strategy.StrategyName.Should().Be(expectedStrategy);
        route.RuleName.Should().Be(expectedRule);
    }

    /// <summary>
    /// Ranh giới cao điểm (thứ Ba 17:00–21:00, nửa mở) tính theo <b>giờ chi nhánh</b>: cùng các mốc giờ địa phương cho kết quả giống nhau ở chi nhánh
    /// <c>Asia/Ho_Chi_Minh</c> (+07:00) và <c>Europe/Lisbon</c> (+00:00 tháng 11) dù các instant UTC lệch nhau 7 giờ.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [Trait("CatalogId", "UT-05")]
    [InlineData("Asia/Ho_Chi_Minh", 16, 59, false)]
    [InlineData("Asia/Ho_Chi_Minh", 17, 0, true)]
    [InlineData("Asia/Ho_Chi_Minh", 20, 59, true)]
    [InlineData("Asia/Ho_Chi_Minh", 21, 0, false)]
    [InlineData("Europe/Lisbon", 16, 59, false)]
    [InlineData("Europe/Lisbon", 17, 0, true)]
    [InlineData("Europe/Lisbon", 20, 59, true)]
    [InlineData("Europe/Lisbon", 21, 0, false)]
    public void Route_PeakHour_UsesBranchLocalTime_Theory(string timeZoneId, int hour, int minute, bool expectedPeak)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = new DateTime(2026, 11, 24, hour, minute, 0, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        var reservation = DomainTestData.CreateReservation(items: [DomainTestData.CreateItem(start)], timeZone: timeZone);
        using var router = CreateRouter(Routing(Default, Rule("peak-hour", Mock, w => w.PeakHour = true)));

        var route = router.Route(reservation);

        route.Strategy.Strategy.StrategyName.Should().Be(expectedPeak ? Mock : Default);
        route.RuleName.Should().Be(expectedPeak ? "peak-hour" : null);
    }

    /// <summary>Tên strategy trong cấu hình không phân biệt hoa thường (như registry), route trả strategy với tên chuẩn.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Route_StrategyNames_CaseInsensitive()
    {
        using var router = CreateRouter(Routing(
            "DEFAULTDECISIONSTRATEGY",
            Rule("peak", "mockalternativedecisionstrategy", w => w.PeakHour = true)));

        router.Route(Reservation()).Strategy.Strategy.StrategyName.Should().Be(Mock);
        router.Route(Reservation(FridayOffPeak)).Strategy.Strategy.StrategyName.Should().Be(Default);
    }

    /// <summary>Tên strategy lạ ở <c>DefaultStrategy</c> hoặc ở rule: router không dựng được, thông báo nêu tên lạ, khóa cấu hình và danh sách hợp lệ.</summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("default", "Tadss:Routing:DefaultStrategy")]
    [InlineData("rule", "Tadss:Routing:Rules:1:Strategy")]
    public void Constructor_UnknownStrategy_Throws_Theory(string location, string expectedKey)
    {
        var options = location == "default"
            ? Routing("Foo", Rule("peak", Mock, w => w.PeakHour = true))
            : Routing(Default, Rule("peak", Mock, w => w.PeakHour = true), Rule("vip", "Foo", w => w.Tiers = [CustomerTier.DIAMOND]));

        var act = () => CreateRouter(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Foo'*")
            .WithMessage($"*{expectedKey}*")
            .WithMessage($"*{Default}*")
            .WithMessage($"*{Mock}*");
    }

    /// <summary>
    /// Lỗi kiểu mà binder của .NET nuốt khi bind danh sách: phần tử sai kiểu (<c>Channels: ["PHONE"]</c>, hạng lạ, Guid sai) bị bỏ làm rule rộng hơn ý định,
    /// giá trị đơn sai kiểu trong rule (<c>PeakHour: "maybe"</c>) làm mất cả rule. Router không dựng được (thay vì chạy với bảng sai), thông báo nêu đường dẫn
    /// khóa và không in giá trị.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("Tadss:Routing:Rules:0:When:Channels:0", "PHONE")]
    [InlineData("Tadss:Routing:Rules:0:When:Tiers:0", "VIP")]
    [InlineData("Tadss:Routing:Rules:0:When:BranchIds:0", "not-a-guid")]
    [InlineData("Tadss:Routing:Rules:0:When:PeakHour", "maybe")]
    public void Constructor_SwallowedBindingError_Throws_Theory(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tadss:Routing:Rules:0:Name"] = "peak",
                ["Tadss:Routing:Rules:0:Strategy"] = Mock,
                ["Tadss:Routing:Rules:0:When:PeakHour"] = "true",
                [key] = value,
            })
            .Build();

        var act = () => CreateRouter(new ConfigurationRoutingRulesSource(configuration));

        var exception = act.Should().Throw<OptionsValidationException>().Which;
        exception.Message.Should().Contain(key).And.NotContain(value);
    }

    /// <summary>Route chỉ đọc bảng bất biến: cùng nhánh định tuyến trả cùng một đối tượng kết quả (không cấp phát kết quả mới mỗi lần).</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Route_SameBranchOfTable_ReturnsSameRouteInstance()
    {
        using var router = CreateRouter(Routing(Default, Rule("peak", Mock, w => w.PeakHour = true)));

        router.Route(Reservation()).Should().BeSameAs(router.Route(Reservation()));
        router.Route(Reservation(FridayOffPeak)).Should().BeSameAs(router.Route(Reservation(FridayOffPeak)));
    }
}
