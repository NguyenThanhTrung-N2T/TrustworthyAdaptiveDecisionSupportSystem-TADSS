using System.Net;
using FluentAssertions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests.Routing;

/// <summary>
/// IT-14 (A-01): <c>POST /api/v2/decisions</c> trên host thật (PostgreSQL + Redis thật) với <c>Tadss:Routing</c> cấp qua cấu hình host như người vận hành —
/// router thật chọn strategy theo từng request: rule <c>peak-hour</c> theo giờ chi nhánh, rule theo <c>BranchIds</c> chỉ áp cho chi nhánh được chọn;
/// không khớp thì dùng <c>DefaultStrategy</c>. Response giữ trường <c>strategyName</c> như cũ (A-04) và audit ghi cùng tên strategy.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class RoutingEndpointTests : DecisionApiTestBase
{
    private const string Default = "DefaultDecisionStrategy";
    private const string Mock = "MockAlternativeDecisionStrategy";

    private static readonly Guid SelectedBranch = Guid.Parse("7c1d2e3f-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherBranch = Guid.Parse("7c1d2e3f-0000-4000-8000-0000000000b2");

    private IEnumerable<KeyValuePair<string, string?>> _routing = [];

    public RoutingEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides => _routing;

    /// <summary>
    /// Rule <c>peak-hour</c> → MockAlternative: request thứ Ba 18:00 giờ chi nhánh (cao điểm) dùng MockAlternative (cọc 50% giờ cao điểm), request thứ Ba
    /// 10:00 (thấp điểm) rơi về Default (GOLD cọc 30%); hàng audit của từng quyết định mang đúng tên strategy đó.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-14")]
    public async Task PostDecision_PeakHourRule_RoutesByBranchLocalTime()
    {
        _routing =
        [
            new("Tadss:Routing:Rules:0:Name", "peak-hour"),
            new("Tadss:Routing:Rules:0:Strategy", Mock),
            new("Tadss:Routing:Rules:0:When:PeakHour", "true")
        ];
        var vietnam = TimeSpan.FromHours(7);

        var peak = await PostDecisionAsync(DecisionRequests.StartingAt(new DateTimeOffset(2026, 11, 24, 18, 0, 0, vietnam)), NewKey());
        var offPeak = await PostDecisionAsync(DecisionRequests.StartingAt(new DateTimeOffset(2026, 11, 24, 10, 0, 0, vietnam)), NewKey());

        peak.Status.Should().Be(HttpStatusCode.OK);
        offPeak.Status.Should().Be(HttpStatusCode.OK);
        peak.Result.StrategyName.Should().Be(Mock);
        ContractSamples.Required(peak.Result.Deposit).Rate.Should().Be(0.5m);
        offPeak.Result.StrategyName.Should().Be(Default);
        ContractSamples.Required(offPeak.Result.Deposit).Rate.Should().Be(0.3m);
        await AssertAuditStrategiesAsync((peak.Result.DecisionId, Mock), (offPeak.Result.DecisionId, Default));
    }

    /// <summary>
    /// Rule theo <c>BranchIds</c> (chi nhánh thí điểm) → MockAlternative: chỉ request của chi nhánh đó dùng MockAlternative; chi nhánh khác cùng nội dung
    /// (cùng giờ cao điểm, cùng khách) vẫn dùng Default.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-14")]
    public async Task PostDecision_BranchRule_RoutesSelectedBranchOnly()
    {
        _routing =
        [
            new("Tadss:Routing:Rules:0:Name", "branch-pilot"),
            new("Tadss:Routing:Rules:0:Strategy", Mock),
            new("Tadss:Routing:Rules:0:When:BranchIds:0", SelectedBranch.ToString())
        ];

        var selected = await PostDecisionAsync(DecisionRequests.Mutated(root => root["branchId"] = SelectedBranch.ToString()), NewKey());
        var other = await PostDecisionAsync(DecisionRequests.Mutated(root => root["branchId"] = OtherBranch.ToString()), NewKey());

        selected.Status.Should().Be(HttpStatusCode.OK);
        other.Status.Should().Be(HttpStatusCode.OK);
        selected.Result.StrategyName.Should().Be(Mock);
        other.Result.StrategyName.Should().Be(Default);
        await AssertAuditStrategiesAsync((selected.Result.DecisionId, Mock), (other.Result.DecisionId, Default));
        Strategies.InvocationCount.Should().Be(2, "mỗi request chạy đúng strategy được định tuyến một lần");
    }

    /// <summary>Mỗi quyết định có đúng một hàng audit <c>PRIMARY</c> mang tên strategy mong đợi (tra theo <c>decisionId</c>, không theo thứ tự thời gian).</summary>
    private async Task AssertAuditStrategiesAsync(params (Guid DecisionId, string StrategyName)[] expected)
    {
        var rows = await ReadExecutionsAsync();
        rows.Should().HaveCount(expected.Length);
        foreach (var (decisionId, strategyName) in expected)
        {
            rows.Should().ContainSingle(row => row.DecisionId == decisionId).Which.StrategyName.Should().Be(strategyName);
        }
    }
}
