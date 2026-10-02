using System.Net;
using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using StackExchange.Redis;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-13: cache <c>StrategyDecision</c> trên Redis thật. Host test tắt <c>EnableFallbackToMemory</c> nên <c>CACHE_HIT</c> chỉ có thể đến từ Redis;
/// Redis được dọn trước mỗi test. Kiểm tra khóa <c>tadss:v2:…</c> và giá trị lưu (chỉ <c>StrategyDecision</c>, không có <c>decisionId</c>), thứ tự
/// replay → cache, strategy tắt cache không bao giờ hit, và approval window được tính lại theo thời điểm của từng lần đánh giá.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class CacheIntegrationTests : DecisionApiTestBase
{
    private const string CacheKeyPrefix = "tadss:v2:DefaultDecisionStrategy:2.0.0:";
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(60);

    public CacheIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Hai key khác nhau cùng payload: lần 2 là <c>CACHE_HIT</c> từ Redis, <c>decisionId</c> mới, strategy chỉ chạy một lần.</summary>
    [Fact]
    [Trait("CatalogId", "IT-13")]
    public async Task PostDecision_DifferentKeysSamePayload_SecondIsCacheHitFromRealRedisWithNewDecisionId()
    {
        var first = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());
        Clock.Advance(TimeSpan.FromMinutes(1));

        var second = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());

        first.Status.Should().Be(HttpStatusCode.OK);
        second.Status.Should().Be(HttpStatusCode.OK);
        first.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        second.Result.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        second.Result.DecisionId.Should().NotBe(first.Result.DecisionId);
        second.Result.Status.Should().Be(first.Result.Status);
        second.Result.Deposit.Should().BeEquivalentTo(first.Result.Deposit);
        second.Result.EvaluatedAt.Should().Be(InitialInstant.AddMinutes(1), "response được dựng mới, không lấy từ cache");
        Strategies.InvocationCount.Should().Be(1, "lần 2 lấy quyết định của strategy từ cache");

        var rows = await ReadExecutionsAsync();
        rows.Should().HaveCount(2);
        rows.Select(row => row.CacheHit).Should().Equal(false, true);
        rows[1].ExecutionType.Should().Be(ExecutionType.CACHE_HIT);

        var cacheKey = ReadCacheKeys().Should().ContainSingle().Subject;
        cacheKey.Should().StartWith(CacheKeyPrefix);
        cacheKey.Split(':').Should().HaveCount(6, "tadss:v2:{strategy}:{version}:{fingerprint}:{requestHash}");
        var database = Infrastructure.RedisAdmin.GetDatabase();
        var stored = (string?)await database.StringGetAsync(cacheKey);
        using var document = JsonDocument.Parse(stored ?? throw new InvalidOperationException("Key cache không có giá trị."));
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        properties.Should().BeEquivalentTo(["status", "depositRate", "reasonCode", "reasons", "recommendations"],
            "Redis chỉ lưu StrategyDecision, không lưu response");
        properties.Should().NotContain(["decisionId", "evaluatedAt", "deposit", "approvalExpiresAt"]);
        var ttl = await database.KeyTimeToLiveAsync(cacheKey);
        ttl.Should().NotBeNull();
        ttl.GetValueOrDefault().Should().BeLessThanOrEqualTo(DefaultCacheTtl).And.BeGreaterThan(DefaultCacheTtl - TimeSpan.FromMinutes(2));
    }

    /// <summary>Cùng key và payload ở lần 2: replay bản đã lưu (<c>AUTOMATED</c>), không đọc cache dù key cache đã tồn tại.</summary>
    [Fact]
    [Trait("CatalogId", "IT-13")]
    public async Task PostDecision_SameKeySamePayload_ReplayTakesPrecedenceOverCacheHit()
    {
        var key = NewKey();
        var first = await PostDecisionAsync(DecisionRequests.Valid(), key);

        var second = await PostDecisionAsync(DecisionRequests.Valid(), key);

        ReadCacheKeys().Should().ContainSingle("lần 1 đã ghi cache nên replay thắng cache chứ không phải cache chưa có");
        second.Result.DecisionId.Should().Be(first.Result.DecisionId);
        second.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        second.Bytes.Should().Equal(first.Bytes);
        (await ReadExecutionsAsync()).Should().ContainSingle().Which.CacheHit.Should().BeFalse();
    }

    /// <summary>Strategy khai báo không cache: không có key <c>tadss:v2:*</c> trong Redis và hai key khác nhau cùng payload đều chạy strategy.</summary>
    [Fact]
    [Trait("CatalogId", "IT-13")]
    public async Task PostDecision_StrategyWithCacheDisabled_NeverHitsCache()
    {
        Strategies.CachePolicyOverride = StrategyCachePolicy.Disabled;

        var first = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());
        var second = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());

        first.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        second.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        ReadCacheKeys().Should().BeEmpty();
        Strategies.InvocationCount.Should().Be(2);
        (await ReadExecutionsAsync()).Select(row => row.CacheHit).Should().OnlyContain(hit => !hit);
    }

    /// <summary>
    /// Quyết định <c>NEED_APPROVAL</c> lấy từ cache nhưng hạn duyệt tính lại từ <c>now</c> của từng lần đánh giá (<c>now + WindowMinutes</c>); khi tới lúc không còn
    /// đủ <c>MinLeadMinutes</c> trước giờ bắt đầu thì cùng quyết định cache đó thành <c>APPROVED</c> cọc 100%.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-13")]
    public async Task PostDecision_CacheHitRecomputesApprovalWindow()
    {
        var start = InitialInstant.AddHours(5);
        var request = DecisionRequests.HighValue(start);
        var window = TimeSpan.FromMinutes(30);

        var first = await PostDecisionAsync(request, NewKey());
        Clock.Advance(TimeSpan.FromMinutes(7));
        var second = await PostDecisionAsync(request, NewKey());
        // now = start - 100 phút: hạn duyệt theo MinLeadMinutes (start - 120 phút) đã qua
        Clock.SetUtcNow(start.AddMinutes(-100));
        var third = await PostDecisionAsync(request, NewKey());

        first.Result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        first.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        first.Result.ApprovalExpiresAt.Should().Be(InitialInstant + window);

        second.Result.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        second.Result.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        second.Result.ApprovalExpiresAt.Should().Be(InitialInstant.AddMinutes(7) + window, "hạn duyệt tính lại theo now của lần 2");

        third.Result.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        third.Result.Status.Should().Be(DecisionStatus.APPROVED, "không còn đủ thời gian để Manager duyệt");
        third.Result.ApprovalExpiresAt.Should().BeNull();
        V2Samples.Required(third.Result.Deposit).Rate.Should().Be(1m);

        Strategies.InvocationCount.Should().Be(1);
        var rows = await ReadExecutionsAsync();
        rows.Select(row => row.StrategyStatus).Should().OnlyContain(status => status == DecisionStatus.NEED_APPROVAL);
        rows.Select(row => row.ApprovalExpiresAt).Should().Equal(first.Result.ApprovalExpiresAt, second.Result.ApprovalExpiresAt, null);
    }
}
