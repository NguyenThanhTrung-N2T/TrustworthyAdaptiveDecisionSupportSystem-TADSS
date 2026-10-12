using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Strategies;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Infrastructure.Persistence.Entities;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests.Shadow;

/// <summary>
/// IT-12 (INV-8, A-11): shadow trên host thật (PostgreSQL + Redis thật, đồng hồ thật vì Polly cần timer thật). Strategy chính là strategy thật của runtime;
/// strategy shadow là <c>MockAlternativeDecisionStrategy</c> hoặc <see cref="ScriptedStrategy"/> thêm vào registry. Request được gửi cho host bật shadow và cho
/// một host đối chứng cùng database nhưng tắt shadow: response <c>PRIMARY</c> phải giống nhau (trừ <c>decisionId</c>, <c>evaluatedAt</c>); bản <c>SHADOW</c>
/// xuất hiện bất đồng bộ nên được chờ bằng polling có hạn.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class ShadowExecutionTests : DecisionApiTestBase
{
    private const string Mock = "MockAlternativeDecisionStrategy";
    private const string NeedApprovalOverrideBody = """{"action":"APPROVE","deposit":{"rate":0.3},"reason":"Khách quen, chi nhánh bảo lãnh"}""";
    private const string OutcomeBody = """{"outcome":"COMPLETED","occurredAt":"2026-11-20T18:30:00+07:00"}""";

    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    private ScriptedStrategy? _scripted;

    public ShadowExecutionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override bool UseFakeClock => false;

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides =>
    [
        new("Tadss:Shadow:Enabled", "true"),
        new("Tadss:Shadow:Strategy", _scripted?.StrategyName ?? Mock),
        new("Tadss:Shadow:SampleRate", "1"),
    ];

    /// <inheritdoc />
    protected override void ConfigureHostServices(IServiceCollection services)
    {
        if (_scripted is not { } scripted)
        {
            return;
        }

        services.RemoveAll<IStrategyRegistry>();
        services.AddSingleton<IStrategyRegistry>(sp =>
            new ExtendedStrategyRegistry(new ProbeStrategyRegistry(ActivatorUtilities.CreateInstance<StrategyRegistry>(sp)), scripted));
    }

    /// <summary>
    /// Shadow là MockAlternative: response <c>PRIMARY</c> giống lượt đối chứng tắt shadow; sau đó có đúng một bản <c>SHADOW</c> cùng
    /// <c>reservation_id</c>, <c>request_hash</c>, <c>created_at</c>, <c>correlation_id</c> với bản <c>PRIMARY</c>, không khóa idempotency, do strategy shadow tạo.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task PostDecision_ShadowEnabled_StoresShadowRecordAndKeepsResponse()
    {
        var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));

        var primary = await PostDecisionAsync(body, NewKey(), "corr-shadow-01");
        var baseline = await PostBaselineAsync(body);

        primary.Status.Should().Be(HttpStatusCode.OK);
        AssertSameResponse(primary.Result, baseline.Result);
        var shadow = await WaitForShadowAsync(count: 1);
        var primaryRow = (await ReadExecutionsAsync()).Single(row => row.DecisionId == primary.Result.DecisionId);
        shadow.Should().ContainSingle().Which.Should().Match<DecisionExecutionEntity>(row =>
            row.Role == ExecutionRole.SHADOW
            && row.IdempotencyKey == null
            && row.StrategyName == Mock
            && row.ReservationId == primaryRow.ReservationId
            && row.RequestHash == primaryRow.RequestHash
            && row.CreatedAt == primaryRow.CreatedAt
            && row.CorrelationId == "corr-shadow-01"
            && !row.CacheHit);
        primaryRow.Role.Should().Be(ExecutionRole.PRIMARY);
    }

    /// <summary>
    /// Strategy shadow ném lỗi hoặc treo bỏ qua token: response <c>PRIMARY</c> vẫn giống lượt đối chứng, bản <c>PRIMARY</c> vẫn lưu, bản <c>SHADOW</c> là
    /// <c>FALLBACK</c> (<c>APPROVED</c> cọc 100%), và strategy chính tiếp tục chạy bình thường (breaker riêng theo strategy).
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-12")]
    [InlineData("throw")]
    [InlineData("hang")]
    public async Task PostDecision_ShadowStrategyThrowsOrHangs_PrimaryUnaffected_Theory(string behavior)
    {
        _scripted = new ScriptedStrategy
        {
            Behavior = behavior == "throw"
                ? (_, _) => throw new InvalidOperationException("Strategy shadow đang hỏng.")
                : async (_, _) =>
                {
                    await Task.Delay(Timeout.Infinite);
                    return ScriptedStrategy.Approve();
                },
        };
        var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));

        var primary = await PostDecisionAsync(body, NewKey());
        var baseline = await PostBaselineAsync(body);

        AssertSameResponse(primary.Result, baseline.Result);
        primary.Result.FallbackUsed.Should().BeFalse("lỗi của shadow không lan sang strategy chính");
        var shadow = (await WaitForShadowAsync(count: 1)).Single();
        shadow.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        shadow.FallbackUsed.Should().BeTrue();
        shadow.Status.Should().Be(DecisionStatus.APPROVED);
        shadow.DepositRate.Should().Be(1.0m);
        var followUp = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(9)), NewKey());
        followUp.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED, "strategy chính vẫn chạy bình thường");
        (await ReadExecutionsAsync()).Should().Contain(row => row.DecisionId == primary.Result.DecisionId && row.Role == ExecutionRole.PRIMARY);
    }

    /// <summary>
    /// Shadow trả <c>NEED_APPROVAL</c>: bản <c>SHADOW</c> không vào hàng chờ duyệt của chi nhánh, và override/outcome với <c>decisionId</c> của nó
    /// đều 404 (INV-8): bản <c>SHADOW</c> không bao giờ lộ cho client như một quyết định thật.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task ShadowRecord_NotInPendingQueue_AndNotOverridable()
    {
        _scripted = new ScriptedStrategy
        {
            Behavior = (_, _) => Task.FromResult(StrategyDecision.RequireApproval(
                "FI_RULE_NEED_APPROVAL",
                ["Strategy shadow đòi Manager duyệt."],
                [new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Cần Manager duyệt.")])),
        };
        var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));
        var branchId = ContractSamples.DeserializeRequest(body).BranchId;

        var primary = await PostDecisionAsync(body, NewKey());
        var shadow = (await WaitForShadowAsync(count: 1)).Single();

        shadow.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        var pending = await GetPendingApprovalsAsync($"?branchId={branchId}");
        pending.Pending.Items.Should().NotContain(item => item.DecisionId == shadow.DecisionId);
        pending.Pending.TotalItems.Should().Be(0, "bản PRIMARY là APPROVED và bản SHADOW không vào hàng chờ");
        (await PostOverrideAsync(shadow.DecisionId, NeedApprovalOverrideBody, NewKey(), "manager-01")).Status.Should().Be(HttpStatusCode.NotFound);
        (await PostOutcomeAsync(shadow.DecisionId, OutcomeBody, NewKey())).Status.Should().Be(HttpStatusCode.NotFound);
        primary.Result.DecisionId.Should().NotBe(shadow.DecisionId);
    }

    /// <summary>
    /// Gửi lại cùng idempotency key (replay) không tạo thêm bản <c>SHADOW</c>. Worker đọc hàng đợi theo thứ tự nên sau khi request kế tiếp có bản <c>SHADOW</c>,
    /// mọi việc xếp hàng trước đó (kể cả việc từ replay nếu có) đã được xử lý: chỉ còn đúng hai bản (request đầu và request kế tiếp).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-12")]
    public async Task PostDecision_IdempotentReplay_NoSecondShadow()
    {
        var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));
        var key = NewKey();

        var first = await PostDecisionAsync(body, key);
        await WaitForShadowAsync(count: 1);
        var replay = await PostDecisionAsync(body, key);
        var next = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(8)), NewKey());

        replay.Result.DecisionId.Should().Be(first.Result.DecisionId);
        var shadows = await WaitForShadowAsync(count: 2);
        shadows.Should().HaveCount(2);
        shadows.Select(row => row.CreatedAt).Should().OnlyHaveUniqueItems();
        next.Result.DecisionId.Should().NotBe(first.Result.DecisionId);
    }

    /// <summary>Response của hai lần gọi giống nhau trừ <c>decisionId</c> (UUID mới mỗi lần) và <c>evaluatedAt</c> (mốc thời gian đánh giá).</summary>
    private static void AssertSameResponse(DecisionResultDto actual, DecisionResultDto expected) =>
        actual.Should().BeEquivalentTo(
            expected,
            options => options.Excluding(result => result.DecisionId).Excluding(result => result.EvaluatedAt));

    /// <summary>
    /// Gửi <paramref name="body"/> cho host đối chứng (tắt shadow). Xóa Redis trước để lượt đối chứng chạy strategy thật thay vì đọc <c>CACHE_HIT</c>
    /// từ kết quả host bật shadow vừa ghi (hai host dùng chung Redis).
    /// </summary>
    private async Task<DecisionHttpResponse> PostBaselineAsync(string body)
    {
        await Infrastructure.FlushRedisAsync();
        return await PostDecisionAsync(body, NewKey(), client: CreateShadowDisabledClient());
    }

    /// <summary>Client của host đối chứng: cùng database và Redis nhưng tắt shadow (và dùng registry/router thật).</summary>
    private HttpClient CreateShadowDisabledClient() =>
        CreateFactory(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Tadss:Shadow:Enabled", "false")]))).CreateClient();

    /// <summary>Chờ (polling, tối đa 10 giây) tới khi có đúng <paramref name="count"/> bản <c>SHADOW</c>; trả về các bản đó theo thời điểm tạo.</summary>
    private async Task<List<DecisionExecutionEntity>> WaitForShadowAsync(int count)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (true)
        {
            var shadows = (await ReadExecutionsAsync()).Where(row => row.Role == ExecutionRole.SHADOW).ToList();
            if (shadows.Count >= count)
            {
                return shadows;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Chưa có đủ {count} bản SHADOW sau {SettleTimeout.TotalSeconds} giây (hiện có {shadows.Count}).");
            }

            await Task.Delay(50);
        }
    }
}
