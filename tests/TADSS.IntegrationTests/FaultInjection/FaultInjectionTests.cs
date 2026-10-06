using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Common;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Common;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Resilience;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// Ma trận lỗi v2 của strategy / cache / client (F1–F5, F7 ↔ FI-01…FI-04, FI-06, FI-10 trong RESILIENCE_AND_FAULT_TAXONOMY) chạy qua pipeline HTTP thật
/// trên PostgreSQL + Redis thật, bằng <b>đồng hồ thật</b> (Polly cần timer thật). Strategy giả là <c>IReservationDecisionStrategy</c> v2 thật được registry giả
/// trả về; mọi thay thế DI chỉ nằm trong host test (C-10). Mỗi test chọn cấu hình <c>Tadss:Resilience</c> riêng, đều qua <c>ResilienceOptionsValidator</c>
/// (ngân sách <c>Inner × (Retry + 1) + Delay × Retry ≤ Outer</c>). F6 (<c>DbUpdateException</c> → 500) không chuyển sang v2: lỗi non-transient của store → 500
/// được phủ bởi test store (Task 6) và bộ phân loại (UT-08); mất kết nối PostgreSQL ở <see cref="PersistenceFaultInjectionTests"/>.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class FaultInjectionTests : FaultInjectionTestBase
{
    /// <summary>
    /// Dung sai thời gian thực cho ngưỡng "p99 endpoint ≤ 700 ms" của SC2 (EXPERIMENTAL_DESIGN…, mục SC2): endpoint = pipeline 500 ms + 200 ms cho
    /// ghi audit, HTTP in-process và độ trễ lập lịch của máy CI. Ngân sách của chính pipeline được khẳng định chặt hơn qua <c>execution_time_ms</c> ≤ 500.
    /// </summary>
    private const int EndpointToleranceMs = 200;

    /// <summary>Ngân sách tổng của pipeline (OuterTimeoutMs của cấu hình sản xuất, SLA 500 ms).</summary>
    private const int PipelineBudgetMs = 500;

    private const string SecretMessage = "Fatal third-party AI provider crashed: api-key=sk-secret-123";

    private const string OverrideApproveBody = """{"action":"APPROVE","deposit":{"rate":0.3},"reason":"Khách quen, chi nhánh bảo lãnh"}""";

    public FaultInjectionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>
    /// F1 (FI-01): lần thử đầu chậm hơn inner timeout (250 ms &gt; 100 ms), lần hai nhanh. Pipeline retry trong ngân sách
    /// (100 × 3 + 20 × 2 = 340 ms ≤ Outer 1000 ms) và trả kết quả của strategy: <c>AUTOMATED</c>, không fallback, strategy được gọi 2 lần.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-01")]
    public async Task F1_PerAttemptTimeoutInBudget_RetriesAndReturnsAutomated()
    {
        Resilience = new ResilienceOptions { OuterTimeoutMs = 1000, InnerTimeoutMs = 100, MaxRetryAttempts = 2, RetryBaseDelayMs = 20 };
        Strategy.Behavior = async (attempt, token) =>
        {
            if (attempt == 1)
            {
                await Task.Delay(250, token);
            }

            return ScriptedStrategy.Approve();
        };

        var response = await PostDecisionAsync(UpcomingRequest(), NewKey());

        response.Status.Should().Be(HttpStatusCode.OK);
        var result = response.Result;
        result.Status.Should().Be(DecisionStatus.APPROVED);
        result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        result.FallbackUsed.Should().BeFalse();
        result.ReasonCode.Should().Be("FI_RULE_APPROVED");
        Strategy.InvocationCount.Should().Be(2, "lần đầu quá inner timeout, lần hai thành công");

        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        row.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        row.FallbackUsed.Should().BeFalse();
        row.FailureReason.Should().BeNull();
    }

    /// <summary>
    /// F2 (FI-02): strategy luôn chậm (inner 50 ms, retry 2 × 15 ms) ⇒ <c>APPROVED</c> cọc 100% / <c>FALLBACK</c> / <c>FALLBACK_STRATEGY_TIMEOUT</c>, strategy được gọi 3 lần.
    /// Ngân sách khẳng định ở hai lớp: thời gian pipeline ghi trong audit (<c>execution_time_ms</c>) ≤ 500 ms, và thời gian thực của cả request
    /// ≤ 500 ms + <see cref="EndpointToleranceMs"/> (ngưỡng p99 endpoint của SC2). Host được làm nóng trước bằng một request thật (xem <c>WarmUpHostAsync</c>) để phép đo không chứa chi phí khởi động lạnh.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-02")]
    public async Task F2_AlwaysSlowStrategy_FallsBackWithTimeoutReason()
    {
        Resilience = new ResilienceOptions { OuterTimeoutMs = PipelineBudgetMs, InnerTimeoutMs = 50, MaxRetryAttempts = 2, RetryBaseDelayMs = 15 };
        Strategy.Behavior = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return ScriptedStrategy.Approve();
        };
        var warmUpKey = await WarmUpHostAsync();
        var invocationsBefore = Strategy.InvocationCount;
        var key = NewKey();
        var body = UpcomingRequest();

        var stopwatch = Stopwatch.StartNew();
        var response = await PostDecisionAsync(body, key);
        stopwatch.Stop();

        response.Status.Should().Be(HttpStatusCode.OK);
        (Strategy.InvocationCount - invocationsBefore).Should().Be(3, "lượt đầu và 2 lần retry");

        var row = (await ReadExecutionsAsync()).Should().ContainSingle(execution => execution.IdempotencyKey == key).Subject;
        row.IdempotencyKey.Should().NotBe(warmUpKey);
        AssertFallbackFullDeposit(response.Result, row, body, ReasonCodes.FallbackStrategyTimeout);
        row.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyTimeout);
        row.ExecutionTimeMs.Should().BeLessThanOrEqualTo(PipelineBudgetMs, "thời gian pipeline không được vượt ngân sách 500 ms");
        row.ExecutionTimeMs.Should().BeGreaterThanOrEqualTo(150, "đã chờ đủ 3 lượt inner timeout 50 ms (không fallback sớm)");
        stopwatch.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(PipelineBudgetMs + EndpointToleranceMs);
    }

    /// <summary>
    /// F5 (FI-05): strategy không hợp tác — chặn <b>đồng bộ</b> 3 s và bỏ qua token — với Outer 500 ms, Inner 100 ms, retry 2 × 20 ms (xem ghi chú dư ngân sách trong test).
    /// <c>IsolatedStrategyInvoker</c> cắt từng lượt ở inner timeout nên request vẫn kết thúc <c>APPROVED</c> cọc 100% / <c>FALLBACK_STRATEGY_TIMEOUT</c>
    /// trong 500 ms + 300 ms dung sai, và mỗi lượt bị bỏ rơi được ghi metric <c>tadss.strategy.abandoned</c>.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-05")]
    public async Task F5_UncooperativeStrategy_IsCutWithinBudget()
    {
        const int uncooperativeToleranceMs = 300;
        // Gần cấu hình mặc định (Outer 500, retry 2) nhưng Inner 100 / delay 20 để còn dư ngân sách (340 ms): timer Windows có độ phân giải ~15 ms
        // nên mặc định đúng (420 ms) sát ngưỡng Outer 500 ms và đôi lúc chạm Outer timeout (504) dù pipeline cắt đúng.
        Resilience = new ResilienceOptions { OuterTimeoutMs = PipelineBudgetMs, InnerTimeoutMs = 100, MaxRetryAttempts = 2, RetryBaseDelayMs = 20 };
        Strategy.Behavior = (_, _) =>
        {
            // Chặn đồng bộ, không nhìn token: nếu pipeline không cô lập thì request bị kẹt 3 s
            Thread.Sleep(3000);
            return Task.FromResult(ScriptedStrategy.Approve());
        };
        var warmUpKey = await WarmUpHostAsync();
        var invocationsBefore = Strategy.InvocationCount;
        using var recorder = new MetricRecorder(Host.Services.GetRequiredService<TadssMetrics>().Meter);
        var key = NewKey();
        var body = UpcomingRequest();

        var stopwatch = Stopwatch.StartNew();
        var response = await PostDecisionAsync(body, key);
        stopwatch.Stop();

        response.Status.Should().Be(HttpStatusCode.OK);
        var row = (await ReadExecutionsAsync()).Should().ContainSingle(execution => execution.IdempotencyKey == key).Subject;
        row.IdempotencyKey.Should().NotBe(warmUpKey);
        AssertFallbackFullDeposit(response.Result, row, body, ReasonCodes.FallbackStrategyTimeout);
        stopwatch.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(PipelineBudgetMs + uncooperativeToleranceMs, "strategy chặn 3 s phải bị cắt trong ngân sách 500 ms");
        (Strategy.InvocationCount - invocationsBefore).Should().Be(3, "lượt đầu và 2 lần retry");
        recorder.Sum(TadssMetrics.StrategyAbandoned, ("strategy", Strategy.StrategyName)).Should().Be(3, "mỗi lượt bị cắt đều bị bỏ rơi");
    }

    /// <summary>
    /// F3 (FI-03): strategy ném exception (retry 1 lần rồi fallback) ⇒ <c>FALLBACK_STRATEGY_EXCEPTION</c>; message gốc của exception (có thể chứa bí mật)
    /// không xuất hiện ở response lẫn bản ghi audit — chỉ tên kiểu exception được ghi.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-03")]
    public async Task F3_StrategyThrows_FallsBackWithExceptionReason()
    {
        Resilience = new ResilienceOptions { OuterTimeoutMs = 1000, InnerTimeoutMs = 100, MaxRetryAttempts = 1, RetryBaseDelayMs = 10 };
        Strategy.Behavior = (_, _) => throw new InvalidOperationException(SecretMessage);

        var body = UpcomingRequest();

        var response = await PostDecisionAsync(body, NewKey());

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Body.Should().NotContain(SecretMessage).And.NotContain("sk-secret-123");
        Strategy.InvocationCount.Should().Be(2, "lượt đầu và 1 lần retry");

        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        AssertFallbackFullDeposit(response.Result, row, body, ReasonCodes.FallbackStrategyException);
        row.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyException).And.NotContain("sk-secret-123");
        row.ResponsePayload.Should().NotContain("sk-secret-123");
    }

    /// <summary>
    /// F3 (FI-03, hệ quả nghiệp vụ D-13): quyết định fallback là <c>APPROVED</c> cọc 100% nên không vào hàng chờ duyệt của chi nhánh và không thể
    /// override (override chỉ áp cho <c>NEED_APPROVAL</c>) — <c>POST /override</c> trả 409 <c>NOT_PENDING_APPROVAL</c> và không ghi hàng override.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-03")]
    public async Task F3_FallbackDecision_NotPendingAndOverrideReturns409()
    {
        Resilience = new ResilienceOptions { OuterTimeoutMs = 1000, InnerTimeoutMs = 100, MaxRetryAttempts = 0, RetryBaseDelayMs = 10 };
        Strategy.Behavior = (_, _) => throw new InvalidOperationException("Strategy đang hỏng.");
        var body = UpcomingRequest();
        var branchId = ContractSamples.DeserializeRequest(body).BranchId;

        var posted = await PostDecisionAsync(body, NewKey());
        posted.Status.Should().Be(HttpStatusCode.OK);
        var decision = posted.Result;
        decision.Status.Should().Be(DecisionStatus.APPROVED);
        decision.FallbackUsed.Should().BeTrue();

        var pending = await GetPendingApprovalsAsync($"?branchId={branchId}");
        pending.Status.Should().Be(HttpStatusCode.OK);
        pending.Pending.TotalItems.Should().Be(0, "quyết định fallback không chờ Manager duyệt");
        pending.Pending.Items.Should().BeEmpty();

        var overridden = await PostOverrideAsync(decision.DecisionId, OverrideApproveBody, NewKey(), "manager-01");

        overridden.Status.Should().Be(HttpStatusCode.Conflict);
        overridden.ContentType.Should().Be("application/problem+json");
        overridden.Problem.GetProperty("code").GetString().Should().Be("NOT_PENDING_APPROVAL");
        overridden.Problem.GetProperty("status").GetInt32().Should().Be(409);
        (await ReadOverridesAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// F4 (FI-04): strategy lỗi liên tục ⇒ hai request đầu fallback do exception và làm breaker mở (throughput 2, tỷ lệ lỗi 100%); request thứ ba
    /// fast-fail <c>FALLBACK_CIRCUIT_OPEN</c> mà không gọi strategy. Sau <c>BreakDuration</c> (1 s, đồng hồ thật) và khi strategy đã khỏe, request tiếp theo
    /// là lượt thăm dò half-open → thành công ⇒ breaker đóng, request sau đó chạy strategy bình thường. Chuyển trạng thái được đối chiếu với log của breaker.
    /// Cấu hình: Outer 500, Inner 100, retry 0 (⇒ ngân sách 100 ms), sampling 30 s, break 1 s.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-04")]
    public async Task F4_RepeatedFailures_OpensBreakerAndFastFails_ThenRecovers()
    {
        const int breakDurationSeconds = 1;
        Resilience = new ResilienceOptions
        {
            OuterTimeoutMs = PipelineBudgetMs,
            InnerTimeoutMs = 100,
            MaxRetryAttempts = 0,
            RetryBaseDelayMs = 20,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 2,
            CircuitBreakerBreakDurationSeconds = breakDurationSeconds
        };
        var healthy = false;
        Strategy.Behavior = (_, _) => healthy
            ? Task.FromResult(ScriptedStrategy.Approve())
            : throw new InvalidOperationException("Strategy đang hỏng.");

        var first = await PostDecisionAsync(UpcomingRequest(), NewKey());
        var second = await PostDecisionAsync(UpcomingRequest(), NewKey());
        first.Result.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        second.Result.ReasonCode.Should().Be(ReasonCodes.FallbackStrategyException);
        Strategy.InvocationCount.Should().Be(2);

        // Breaker đã mở: fast-fail, strategy không được gọi thêm
        var fastFailBody = UpcomingRequest();
        var fastFail = await PostDecisionAsync(fastFailBody, NewKey());
        Strategy.InvocationCount.Should().Be(2, "breaker mở thì không gọi strategy");

        // Strategy hồi phục; chờ hết BreakDuration (đồng hồ thật) để lượt kế tiếp là lượt thăm dò half-open
        healthy = true;
        await Task.Delay(TimeSpan.FromSeconds(breakDurationSeconds) + TimeSpan.FromMilliseconds(500));

        var probe = await PostDecisionAsync(UpcomingRequest(), NewKey());
        var closed = await PostDecisionAsync(UpcomingRequest(), NewKey());

        probe.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED, "lượt thăm dò half-open gọi strategy khỏe");
        probe.Result.FallbackUsed.Should().BeFalse();
        closed.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED, "breaker đã đóng");
        Strategy.InvocationCount.Should().Be(4, "thăm dò + request sau khi đóng");

        var rows = await ReadExecutionsAsync();
        rows.Should().HaveCount(5);
        AssertFallbackFullDeposit(fastFail.Result, rows[2], fastFailBody, ReasonCodes.FallbackCircuitOpen);
        rows[2].FailureReason.Should().StartWith(FailureReasonPrefixes.CircuitBreakerOpen);
        rows[2].ExecutionTimeMs.Should().BeLessThan(100, "fast-fail không chờ strategy");

        var transitions = PipelineLogs.Messages
            .Select(message => message switch
            {
                _ when message.Contains("NỬA MỞ", StringComparison.Ordinal) => "half-open",
                _ when message.Contains("MỞ trong", StringComparison.Ordinal) => "open",
                _ when message.Contains("ĐÓNG", StringComparison.Ordinal) => "closed",
                _ => string.Empty
            })
            .Where(transition => transition.Length > 0)
            .ToArray();
        transitions.Should().Equal(["open", "half-open", "closed"], "breaker đi qua open → half-open → closed đúng một vòng");
    }

    /// <summary>
    /// F5 (FI-06, phần cô lập lỗi): cache double ném ở cả <c>GetAsync</c> lẫn <c>SetAsync</c> (strategy bật cache). Lỗi cache chỉ là miss: 200, <c>AUTOMATED</c>,
    /// không fallback strategy, có bản ghi audit; hai request cùng nội dung đều chạy strategy (không có CACHE_HIT) và cả hai đường cache đều đã được thử.
    /// Breaker Redis và Toxiproxy (FI-06 đầy đủ, FI-07) thuộc 5.4–5.5.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-06")]
    public async Task F5_RedisDown_CacheErrorsAreMissAndRequestSucceeds()
    {
        var cache = new ThrowingStrategyDecisionCache();
        Cache = cache;
        Strategy.Policy = Shared.DecisionContractSDK.Decisions.StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(5));
        var body = UpcomingRequest();

        var first = await PostDecisionAsync(body, NewKey());
        var second = await PostDecisionAsync(body, NewKey());

        foreach (var response in new[] { first, second })
        {
            response.Status.Should().Be(HttpStatusCode.OK);
            response.Result.Status.Should().Be(DecisionStatus.APPROVED);
            response.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
            response.Result.FallbackUsed.Should().BeFalse("lỗi cache không làm strategy fallback");
        }

        cache.GetCalls.Should().Be(2, "mỗi request đều thử đọc cache");
        cache.SetCalls.Should().Be(2, "mỗi kết quả AUTOMATED đều thử ghi cache");
        Strategy.InvocationCount.Should().Be(2, "cache hỏng ⇒ miss, strategy chạy cho cả hai request");
        var rows = await ReadExecutionsAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.ExecutionType == ExecutionType.AUTOMATED && !row.CacheHit && !row.FallbackUsed);
    }

    /// <summary>
    /// F6 (FI-06, breaker Redis): Redis đi qua Toxiproxy và bị ngắt. Mọi request vẫn 200 <c>AUTOMATED</c> (lỗi cache không làm strategy fallback); sau vài lệnh lỗi
    /// breaker Redis mở (log "breaker Redis MỞ") và các request sau bỏ qua Redis: <c>tadss.cache.requests{result=bypass}</c> tăng, strategy vẫn chạy bình thường.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-06")]
    public async Task F6_RedisCut_BreakerOpens_RequestsBypassRedis()
    {
        ProxyRedis = true;
        ConfigureCache = options => options.RedisBreakerMinimumThroughput = 2;
        Strategy.Policy = Shared.DecisionContractSDK.Decisions.StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(5));
        using var recorder = new MetricRecorder(Host.Services.GetRequiredService<TadssMetrics>().Meter);
        (await PostDecisionAsync(UpcomingRequest(), NewKey())).Status.Should().Be(HttpStatusCode.OK, "request đầu đi qua Redis còn sống");
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.RedisProxy, false);

        var attempts = 0;
        while (!CacheLogs.Messages.Any(message => message.Contains("breaker Redis MỞ", StringComparison.Ordinal)))
        {
            attempts++;
            attempts.Should().BeLessThan(20, "breaker Redis phải mở sau vài lệnh lỗi");
            var response = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7 + attempts)), NewKey());
            response.Status.Should().Be(HttpStatusCode.OK);
            response.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
            response.Result.FallbackUsed.Should().BeFalse("lỗi Redis không làm strategy fallback");
        }

        var bypassBefore = recorder.Sum(TadssMetrics.CacheRequests, ("result", TadssMetrics.CacheResultBypass));
        var afterOpen = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(40)), NewKey());

        afterOpen.Status.Should().Be(HttpStatusCode.OK);
        afterOpen.Result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        afterOpen.Result.FallbackUsed.Should().BeFalse();
        recorder.Sum(TadssMetrics.CacheRequests, ("result", TadssMetrics.CacheResultBypass)).Should().BeGreaterThan(bypassBefore, "breaker mở ⇒ bỏ qua Redis");
    }

    /// <summary>
    /// F7 (FI-10): client hủy request khi strategy đang chạy (inner 2000 ms, outer 7000 ms, retry 2 × 30 ms ⇒ ngân sách 6060 ms ≤ 7000 ms). Host phải quan sát
    /// hủy (<c>HttpClient</c> hủy → <c>RequestAborted</c> của TestServer → token của strategy), request kết thúc phía server nhanh hơn nhiều so với inner timeout
    /// (nếu không quan sát hủy, strategy sẽ chạy hết 2 s rồi fallback và ghi audit), không retry và không để lại bản ghi audit.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-10")]
    public async Task F7_CallerCancellation_AbortsWithoutAuditRecord()
    {
        const int cancellationObservedWithinMs = 1500;
        Resilience = new ResilienceOptions { OuterTimeoutMs = 7000, InnerTimeoutMs = 2000, MaxRetryAttempts = 2, RetryBaseDelayMs = 30 };
        Strategy.Behavior = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return ScriptedStrategy.Approve();
        };
        using var cts = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath)
        {
            Content = new StringContent(UpcomingRequest(), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, NewKey());

        var inFlight = Api.SendAsync(request, cts.Token);
        await Strategy.Started.WaitAsync(TimeSpan.FromSeconds(10));
        var canceledAt = Stopwatch.GetTimestamp();
        await cts.CancelAsync();

        var act = async () => await inFlight;
        await act.Should().ThrowAsync<OperationCanceledException>("client nhận hủy ngay, không có response");
        var completedAt = await Completion.FirstCompleted.WaitAsync(TimeSpan.FromSeconds(10));
        await Strategy.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(10));

        Stopwatch.GetElapsedTime(canceledAt, completedAt).TotalMilliseconds.Should().BeLessThan(
            cancellationObservedWithinMs,
            "request phải kết thúc ngay sau khi hủy, không chờ inner timeout 2000 ms");
        Strategy.InvocationCount.Should().Be(1, "hủy của caller không bị retry");
        AuditProbe.InsertAttempts.Should().Be(0, "không bao giờ chạm tới bước ghi audit");
        (await ReadExecutionsAsync()).Should().BeEmpty("hủy giữa chừng không để lại bản ghi audit");
    }
}
