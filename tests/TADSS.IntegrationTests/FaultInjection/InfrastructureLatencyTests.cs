using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// FI-07 / FI-09: hạ tầng <b>chậm</b> (không đứt) đi qua Toxiproxy trên host thật (PostgreSQL + Redis thật, đồng hồ thật). FI-07: Redis chậm hơn timeout lệnh của cache không làm strategy fallback —
/// cache coi là lỗi/bỏ qua và request vẫn <c>AUTOMATED</c> trong thời hạn tính từ cấu hình test; sau ngưỡng breaker Redis mở nên request bỏ qua Redis và nhanh hơn.
/// FI-09: PostgreSQL chậm làm request chậm theo nhưng mọi kết quả đúng: mỗi khóa đúng một bản <c>PRIMARY</c>, replay cùng <c>decisionId</c>, đồng thời trùng khóa vẫn một bản.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class InfrastructureLatencyTests : FaultInjectionTestBase
{
    private const int RedisLatencyMs = 200;
    private const int RedisTimeoutMs = 150;
    private const int PostgresLatencyMs = 300;
    private const int SequentialRequests = 30;

    public InfrastructureLatencyTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>
    /// FI-07: toxic độ trễ 200 ms trên Redis, <c>RedisTimeoutMs = 150</c>, ngưỡng breaker nhỏ; 30 POST tuần tự khác payload. Mọi response 200 <c>AUTOMATED</c> (không <c>FALLBACK</c>);
    /// mỗi request ≤ 2 × <c>RedisTimeoutMs</c> + 400 ms; sau ngưỡng có <c>tadss.cache.requests{result=bypass}</c> &gt; 0 và các request sau ≤ <c>RedisTimeoutMs</c> + 300 ms.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-07")]
    public async Task PostDecision_RedisLatency200Ms_NoFallbackBoundedLatency()
    {
        ProxyRedis = true;
        ConfigureCache = options =>
        {
            options.RedisTimeoutMs = RedisTimeoutMs;
            options.RedisBreakerMinimumThroughput = 2;
        };
        Strategy.Policy = StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(5));
        using var recorder = new MetricRecorder(Host.Services.GetRequiredService<TadssMetrics>().Meter);
        await WarmUpHostAsync();
        await Infrastructure.Toxiproxy.AddLatencyAsync(TadssInfrastructureFixture.RedisProxy, RedisLatencyMs);

        var durations = new List<TimeSpan>();
        var bypassFrom = -1;
        for (var index = 0; index < SequentialRequests; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var response = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(8 + index)), NewKey());
            durations.Add(Stopwatch.GetElapsedTime(started));

            response.Status.Should().Be(HttpStatusCode.OK);
            response.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED, "Redis chậm không được làm strategy fallback");
            response.Result.FallbackUsed.Should().BeFalse();
            if (bypassFrom < 0 && recorder.Sum(TadssMetricNames.CacheRequests, (TadssMetricNames.LabelResult, TadssMetricNames.CacheResultBypass)) > 0)
            {
                bypassFrom = index;
            }
        }

        var beforeBreaker = TimeSpan.FromMilliseconds(2 * RedisTimeoutMs + 400);
        var afterBreaker = TimeSpan.FromMilliseconds(RedisTimeoutMs + 300);
        bypassFrom.Should().BeGreaterThan(-1, "sau ngưỡng breaker Redis mở và request bỏ qua Redis");
        durations.Take(bypassFrom + 1).Should().OnlyContain(duration => duration <= beforeBreaker, "trước khi breaker mở mỗi request ≤ 2 × RedisTimeoutMs + 400 ms");
        durations.Skip(bypassFrom + 1).Should().NotBeEmpty();
        durations.Skip(bypassFrom + 1).Should().OnlyContain(duration => duration <= afterBreaker, "sau khi breaker mở request không chờ Redis nữa");
    }

    /// <summary>
    /// FI-09: toxic độ trễ 300 ms trên PostgreSQL; 10 POST khóa khác nhau rồi gửi lại 5 khóa đó, sau đó 10 POST song song cùng khóa và payload. Mọi response đúng (200, replay cùng <c>decisionId</c>);
    /// đúng 10 + 1 bản <c>PRIMARY</c>; mỗi request ≥ 300 ms; <c>tadss.decision.duration</c> ghi nhận mọi request.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-09")]
    public async Task PostDecision_PostgresLatency300Ms_CorrectSingleRecords()
    {
        ProxyPostgres = true;
        using var recorder = new MetricRecorder(Host.Services.GetRequiredService<TadssMetrics>().Meter);
        var warmUpKey = await WarmUpHostAsync();
        await Infrastructure.Toxiproxy.AddLatencyAsync(TadssInfrastructureFixture.PostgresProxy, PostgresLatencyMs);
        var minimum = TimeSpan.FromMilliseconds(PostgresLatencyMs);

        var first = new List<(string Key, string Body, Guid DecisionId)>();
        for (var index = 0; index < 10; index++)
        {
            var key = NewKey();
            var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(9 + index));
            var started = Stopwatch.GetTimestamp();
            var response = await PostDecisionAsync(body, key);
            Stopwatch.GetElapsedTime(started).Should().BeGreaterThanOrEqualTo(minimum, "mỗi request chạm PostgreSQL nên không thể nhanh hơn độ trễ của toxic");

            response.Status.Should().Be(HttpStatusCode.OK);
            first.Add((key, body, response.Result.DecisionId));
        }

        foreach (var (key, body, decisionId) in first.Take(5))
        {
            var replay = await PostDecisionAsync(body, key);
            replay.Status.Should().Be(HttpStatusCode.OK);
            replay.Result.DecisionId.Should().Be(decisionId, "gửi lại cùng khóa và payload phải trả lại đúng quyết định đã lưu");
        }

        var sharedKey = NewKey();
        var sharedBody = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(30));
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => PostDecisionAsync(sharedBody, sharedKey, client: CreateApiClient()))));

        concurrent.Should().OnlyContain(response => response.Status == HttpStatusCode.OK);
        concurrent.Select(response => response.Result.DecisionId).Distinct().Should().ContainSingle("mười request cùng khóa phải cùng một quyết định");
        var primaries = (await ReadExecutionsAsync()).Where(row => row.Role == ExecutionRole.PRIMARY).ToList();
        primaries.Should().HaveCount(1 + 10 + 1, "một bản của request làm nóng, 10 khóa khác nhau và 1 khóa dùng chung");
        primaries.Select(row => row.IdempotencyKey).Should().OnlyHaveUniqueItems().And.Contain(warmUpKey);
        recorder.Count(TadssMetricNames.DecisionDuration, (TadssMetricNames.LabelStatusCode, "200")).Should().BeGreaterThanOrEqualTo(10 + 5 + 10);
    }
}
