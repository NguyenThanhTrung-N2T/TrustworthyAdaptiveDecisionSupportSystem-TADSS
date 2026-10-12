using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Infrastructure.FaultInjection;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// FI-02 / FI-03 (A-07): lỗi strategy do <b>cấu hình</b> <c>Tadss:Testing:FaultInjection</c> chứ không do double — host <c>Testing</c> với
/// <c>Enabled = true</c> chạy qua đăng ký DI thật (options, validator, composition root bọc <see cref="FaultInjectionStrategyDecorator"/> quanh strategy
/// thật <c>DefaultDecisionStrategy</c>), PostgreSQL + Redis thật và đồng hồ thật. Cấu hình chịu lỗi có dư ngân sách (Outer 500, Inner 100, retry 2 × 20 ms
/// = 340 ms) vì timer Windows có độ phân giải ~15 ms.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class ConfiguredFaultInjectionTests : DecisionApiTestBase
{
    private const int PipelineBudgetMs = 500;

    /// <summary>Dung sai thời gian thực của request (ghi audit, HTTP in-process, độ trễ lập lịch của máy CI) cộng vào ngân sách 500 ms của pipeline.</summary>
    private const int EndpointToleranceMs = 300;

    private string _profile = "None";
    private string _delayMs = "1000";

    public ConfiguredFaultInjectionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override bool UseFakeClock => false;

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides =>
    [
        new("Tadss:Testing:FaultInjection:Enabled", "true"),
        new("Tadss:Testing:FaultInjection:Profile", _profile),
        new("Tadss:Testing:FaultInjection:DelayMs", _delayMs),
        new("Tadss:Resilience:OuterTimeoutMs", PipelineBudgetMs.ToString()),
        new("Tadss:Resilience:InnerTimeoutMs", "100"),
        new("Tadss:Resilience:MaxRetryAttempts", "2"),
        new("Tadss:Resilience:RetryBaseDelayMs", "20")
    ];

    /// <summary>
    /// FI-03: <c>Profile = Throw</c> làm strategy thật ném exception ở mọi lượt ⇒ 200 <c>APPROVED</c> cọc 100% / <c>FALLBACK_STRATEGY_EXCEPTION</c>; message cố định
    /// của decorator không rò ra response.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-03")]
    public async Task PostDecision_ConfiguredThrowProfile_FallsBackFullDeposit()
    {
        _profile = "Throw";
        var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));

        var response = await PostDecisionAsync(body, NewKey());

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Body.Should().NotContain(FaultInjectionStrategyDecorator.ThrowMessage);
        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        AssertFallbackFullDeposit(response.Result, row, body, ReasonCodes.FallbackStrategyException);
        row.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyException).And.NotContain(FaultInjectionStrategyDecorator.ThrowMessage);
        Strategies.InvocationCount.Should().Be(3, "lượt đầu và 2 lần retry đều đi qua decorator của host");
    }

    /// <summary>
    /// FI-02: <c>Profile = Delay</c>, <c>DelayMs = 2000</c> làm mỗi lượt vượt inner timeout 100 ms ⇒ <c>APPROVED</c> cọc 100% / <c>FALLBACK_STRATEGY_TIMEOUT</c>,
    /// thời gian pipeline trong audit ≤ 500 ms và thời gian thực của request ≤ 500 ms + dung sai. Host được làm nóng bằng một request trước phép đo.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "FI-02")]
    public async Task PostDecision_ConfiguredDelayProfile_FallsBackWithinBudget()
    {
        _profile = "Delay";
        _delayMs = "2000";
        var warmUp = await PostDecisionAsync(DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7)), NewKey());
        warmUp.Status.Should().Be(HttpStatusCode.OK, "request làm nóng cũng đi qua pipeline và fallback");
        var key = NewKey();
        var body = DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(7));

        var stopwatch = Stopwatch.StartNew();
        var response = await PostDecisionAsync(body, key);
        stopwatch.Stop();

        response.Status.Should().Be(HttpStatusCode.OK);
        var row = (await ReadExecutionsAsync()).Should().ContainSingle(execution => execution.IdempotencyKey == key).Subject;
        AssertFallbackFullDeposit(response.Result, row, body, ReasonCodes.FallbackStrategyTimeout);
        row.FailureReason.Should().StartWith(FailureReasonPrefixes.StrategyTimeout);
        row.ExecutionTimeMs.Should().BeLessThanOrEqualTo(PipelineBudgetMs, "thời gian pipeline không được vượt ngân sách 500 ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(PipelineBudgetMs + EndpointToleranceMs, "Delay 2000 ms phải bị cắt trong ngân sách, không chờ hết 2 s");
    }
}
