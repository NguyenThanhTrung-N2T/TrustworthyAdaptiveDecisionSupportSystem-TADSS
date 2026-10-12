using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Options;
using TADSS.Application.Routing;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Options;

/// <summary>
/// UT-04 (A-01): <see cref="StrategyRoutingOptionsValidator"/> chặn cấu hình <c>Tadss:Routing</c> sai — <c>DefaultStrategy</c> rỗng, rule thiếu tên,
/// tên trùng (không phân biệt hoa thường) hoặc quá 64 ký tự, <c>Strategy</c> rỗng, <c>When</c> không điều kiện, giá trị enum không định nghĩa —
/// với thông báo nêu đúng đường dẫn khóa cấu hình. Thay cho test của <c>StrategyOptionsValidator</c> đã gỡ (A-03).
/// </summary>
public class StrategyRoutingOptionsValidatorTests
{
    private const string Default = "DefaultDecisionStrategy";
    private const string Mock = "MockAlternativeDecisionStrategy";

    private static RoutingRuleOptions PeakRule(string name = "peak-hour", string strategy = Mock) =>
        new() { Name = name, Strategy = strategy, When = new RoutingConditionOptions { PeakHour = true } };

    private static StrategyRoutingOptions Scenario(string scenario) => scenario switch
    {
        "blank-default" => new() { DefaultStrategy = "  ", Rules = [PeakRule()] },
        "missing-name" => new() { Rules = [PeakRule(name: "")] },
        "duplicate-name-ignore-case" => new() { Rules = [PeakRule(name: "peak-hour"), PeakRule(name: "PEAK-HOUR", strategy: Default)] },
        "name-65-chars" => new() { Rules = [PeakRule(name: new string('r', 65))] },
        "blank-strategy" => new() { Rules = [PeakRule(strategy: " ")] },
        "rule-without-condition" => new() { Rules = [new RoutingRuleOptions { Name = "catch-all", Strategy = Mock }] },
        "undefined-tier" => new()
        {
            Rules = [new RoutingRuleOptions { Name = "vip", Strategy = Mock, When = new RoutingConditionOptions { Tiers = [(CustomerTier)99] } }]
        },
        "undefined-channel" => new()
        {
            Rules = [new RoutingRuleOptions { Name = "phone", Strategy = Mock, When = new RoutingConditionOptions { Channels = [(Channel)7] } }]
        },
        "valid" => new()
        {
            DefaultStrategy = Default,
            Rules =
            [
                PeakRule(name: new string('p', 64)),
                new RoutingRuleOptions
                {
                    Name = "branch-guest-walk-in",
                    Strategy = Default,
                    When = new RoutingConditionOptions
                    {
                        BranchIds = [Guid.NewGuid()],
                        Channels = [Channel.WALK_IN],
                        Tiers = [CustomerTier.STANDARD],
                        IsGuest = true
                    }
                }
            ]
        },
        "valid-no-rules" => new(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Kịch bản không tồn tại.")
    };

    /// <summary>
    /// Sáu ca sai của A-01 (và hai ca enum không định nghĩa) bị từ chối với thông báo nêu đường dẫn khóa; cấu hình hợp lệ (tên đúng 64 ký tự, đủ loại điều kiện)
    /// và cấu hình không rule nào đều đạt.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("blank-default", "Tadss:Routing:DefaultStrategy")]
    [InlineData("missing-name", "Tadss:Routing:Rules:0:Name")]
    [InlineData("duplicate-name-ignore-case", "Tadss:Routing:Rules:1:Name")]
    [InlineData("name-65-chars", "Tadss:Routing:Rules:0:Name")]
    [InlineData("blank-strategy", "Tadss:Routing:Rules:0:Strategy")]
    [InlineData("rule-without-condition", "Tadss:Routing:Rules:0:When")]
    [InlineData("undefined-tier", "Tadss:Routing:Rules:0:When:Tiers:0")]
    [InlineData("undefined-channel", "Tadss:Routing:Rules:0:When:Channels:0")]
    [InlineData("valid", null)]
    [InlineData("valid-no-rules", null)]
    public void Validate_Theory(string scenario, string? expectedKey)
    {
        var result = new StrategyRoutingOptionsValidator().Validate(null, Scenario(scenario));

        if (expectedKey is null)
        {
            result.Succeeded.Should().BeTrue(result.FailureMessage);
            return;
        }

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain(expectedKey);
    }

    /// <summary>
    /// Qua host thật (<c>ValidateOnStart</c> trong <c>AddApplication</c>): <c>DefaultStrategy</c> hợp lệ (kể cả strategy thay thế) thì host khởi động và router
    /// dùng nó làm mặc định. Thay cho <c>StrategyOptions_ValidActiveStrategyName_StartsHost_Theory</c>.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData(Default)]
    [InlineData(Mock)]
    public async Task RoutingOptions_ValidDefaultStrategy_StartsHost_Theory(string defaultStrategy)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new("Tadss:Routing:DefaultStrategy", defaultStrategy), TestConnectionStrings.Pair]);
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetRequiredService<IOptions<StrategyRoutingOptions>>().Value.DefaultStrategy.Should().Be(defaultStrategy);
        var route = host.Services.GetRequiredService<IStrategyRouter>().Route(DomainTestData.CreateReservation());
        route.Strategy.Strategy.StrategyName.Should().Be(defaultStrategy);
        route.RuleName.Should().BeNull();
    }
}
