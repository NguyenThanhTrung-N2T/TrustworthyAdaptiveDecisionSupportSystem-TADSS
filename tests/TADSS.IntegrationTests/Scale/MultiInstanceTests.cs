using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.Options;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.Scale;

/// <summary>
/// SCALE-1 (P5): hai host TADSS độc lập (DI, <c>IMemoryCache</c>, circuit breaker riêng) dùng chung một PostgreSQL và một Redis thật, như hai instance
/// sau cùng một load balancer. Chứng minh: idempotency đúng qua unique index của DB (không dựa trạng thái trong tiến trình), cache Redis dùng chung
/// giữa các instance, và circuit breaker của strategy là trạng thái theo từng instance. Host thứ nhất là host mặc định của <see cref="FaultInjectionTestBase"/>
/// (strategy <see cref="FaultInjectionTestBase.Strategy"/>); host thứ hai có strategy riêng cùng tên và phiên bản nên khóa cache trùng nhau.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class MultiInstanceTests : FaultInjectionTestBase
{
    private const int Concurrency = 50;

    private readonly List<HttpClient> _secondHostClients = [];

    public MultiInstanceTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Strategy giả của host thứ hai (riêng, không dùng chung trạng thái với <see cref="FaultInjectionTestBase.Strategy"/>).</summary>
    private ScriptedStrategy SecondStrategy { get; } = new();

    /// <inheritdoc />
    public override async Task DisposeAsync()
    {
        _secondHostClients.ForEach(client => client.Dispose());
        await base.DisposeAsync();
    }

    /// <summary>
    /// 50 request cùng idempotency key và payload chia đều hai host, gửi đồng thời: mọi response 200 giống từng byte, cùng <c>decisionId</c>;
    /// database có đúng một bản <c>PRIMARY</c> cho key đó.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SCALE-1")]
    public async Task SameIdempotencyKey_50ParallelAcrossTwoHosts_SingleRecordSameResponse()
    {
        // Ca này kiểm idempotency giữa hai host, không kiểm độ trễ (đã có LT/FI): ngân sách rộng để 50 request đồng thời trên máy đang bận
        // (cả stack đánh giá chạy cùng lúc) không vượt OuterTimeoutMs mặc định 500 ms thành 504
        Resilience = new ResilienceOptions { OuterTimeoutMs = 10_000, InnerTimeoutMs = 5_000, MaxRetryAttempts = 0, RetryBaseDelayMs = 30 };
        var key = NewKey();
        var body = UpcomingRequest();
        var hostA = CreateApiClient();
        var hostB = CreateSecondHostClient();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(0, Concurrency)
            .Select(index => Task.Run(async () =>
            {
                await startGate.Task;
                return await PostDecisionAsync(body, key, client: index % 2 == 0 ? hostA : hostB);
            }))
            .ToList();
        startGate.SetResult();
        var responses = await Task.WhenAll(requests);

        responses.Select(response => response.Status).Should().OnlyContain(status => status == HttpStatusCode.OK);
        var winner = responses[0].Result.DecisionId;
        responses.Select(response => response.Result.DecisionId).Should().OnlyContain(id => id == winner);
        responses.Select(response => response.Body).Distinct().Should().ContainSingle("50 response phải giống nhau");
        var primaries = (await ReadExecutionsAsync()).Where(row => row.IdempotencyKey == key).ToList();
        primaries.Should().ContainSingle().Which.DecisionId.Should().Be(winner);
    }

    /// <summary>
    /// Strategy bật cache; host A ghi kết quả vào Redis, host B nhận request khác key nhưng cùng payload: <c>CACHE_HIT</c> từ Redis và strategy của host B không được gọi.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SCALE-1")]
    public async Task CacheWrittenOnHostA_ReadOnHostB()
    {
        var cachePolicy = StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(5));
        Strategy.Policy = cachePolicy;
        SecondStrategy.Policy = cachePolicy;
        var body = UpcomingRequest();
        var hostB = CreateSecondHostClient();

        var onA = await PostDecisionAsync(body, NewKey());
        var onB = await PostDecisionAsync(body, NewKey(), client: hostB);

        onA.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        Strategy.InvocationCount.Should().Be(1);
        onB.Status.Should().Be(HttpStatusCode.OK);
        onB.Result.ExecutionType.Should().Be(ExecutionType.CACHE_HIT, "kết quả host A ghi được host B đọc từ Redis dùng chung");
        SecondStrategy.InvocationCount.Should().Be(0, "host B không chạy strategy khi đã có kết quả trong Redis");
        ReadCacheKeys().Should().ContainSingle();
    }

    /// <summary>
    /// Strategy của host A luôn lỗi tới khi breaker của host A mở (host A: <c>FALLBACK_CIRCUIT_OPEN</c>); strategy của host B khỏe nên host B vẫn chạy bình thường
    /// (<c>AUTOMATED</c>): trạng thái breaker không chia sẻ giữa các instance.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SCALE-1")]
    public async Task BreakerOpenOnHostA_HostBRunsStrategy()
    {
        Resilience = new ResilienceOptions
        {
            OuterTimeoutMs = 1000,
            InnerTimeoutMs = 100,
            MaxRetryAttempts = 0,
            RetryBaseDelayMs = 20,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 2,
            CircuitBreakerBreakDurationSeconds = 30,
        };
        Strategy.Behavior = (_, _) => throw new InvalidOperationException("Strategy host A đang hỏng.");
        var hostB = CreateSecondHostClient();

        var first = await PostDecisionAsync(UpcomingRequest(), NewKey());
        var second = await PostDecisionAsync(UpcomingRequest(), NewKey());
        var third = await PostDecisionAsync(UpcomingRequest(), NewKey());
        var onB = await PostDecisionAsync(UpcomingRequest(), NewKey(), client: hostB);

        first.Result.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        second.Result.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        third.Result.ReasonCode.Should().Be(ReasonCodes.FallbackCircuitOpen, "breaker của host A đã mở");
        Strategy.InvocationCount.Should().Be(2, "breaker mở thì host A không gọi strategy nữa");
        onB.Status.Should().Be(HttpStatusCode.OK);
        onB.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED, "breaker của host A không ảnh hưởng host B");
        onB.Result.FallbackUsed.Should().BeFalse();
        SecondStrategy.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// Dựng host thứ hai từ cùng factory gốc (cùng PostgreSQL và Redis): strategy giả riêng là strategy duy nhất và là mặc định của router,
    /// cấu hình chịu lỗi giống host thứ nhất; đồng hồ thật như host thứ nhất. Trả về client mặc định (gửi API key mặc định).
    /// </summary>
    private HttpClient CreateSecondHostClient()
    {
        var client = CreateFactory(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                [new KeyValuePair<string, string?>("Tadss:Routing:DefaultStrategy", SecondStrategy.StrategyName)]));
            builder.ConfigureTestServices(services =>
            {
                services.Configure<ResilienceOptions>(ApplyResilience);
                services.RemoveAll<IStrategyRegistry>();
                services.AddSingleton<IStrategyRegistry>(new ScriptedStrategyRegistry(SecondStrategy));
            });
        }).CreateClient();
        _secondHostClients.Add(client);
        return client;
    }
}
