using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Decisions;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.Resilience;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-11/UT-13: <see cref="StrategyEvaluator"/> dựng khóa cache, đọc cache (lỗi cache chỉ là miss), chạy pipeline khi miss và chỉ ghi cache
/// kết quả <c>AUTOMATED</c>. Cache, pipeline và đồng hồ là test double viết tay; chỉ hủy của chính caller được lan ra.
/// </summary>
public class StrategyEvaluatorTests
{
    private const string IdempotencyKey = "idem-key-0001";
    private const string StrategyFingerprint = "0123456789abcdef";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    public static TheoryData<bool, bool> CacheFailures => new()
    {
        { true, false },
        { false, true },
        { true, true }
    };

    private static DelegateStrategy ApproveStrategy(StrategyCachePolicy? cachePolicy = null) =>
        new(
            (_, _, _) => Task.FromResult(StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Khung giờ cao điểm áp dụng cọc 50%."])),
            cachePolicy);

    private static string CacheKeyOf(RegisteredStrategy strategy, string requestHash) =>
        StrategyDecisionCacheKey.Build(
            strategy.Strategy.StrategyName,
            strategy.Strategy.StrategyVersion,
            strategy.OptionsFingerprint,
            requestHash);

    /// <summary>Bộ phụ thuộc giả dùng chung cho một lần dựng <see cref="StrategyEvaluator"/>.</summary>
    private sealed class Fixture
    {
        private readonly DecisionRequestDto _request = ContractSamples.DeserializeRequest(ContractSamples.RequestJson);

        public Fixture(DelegateStrategy strategy, FakeStrategyPipeline? pipeline = null, string fingerprint = StrategyFingerprint)
        {
            Registered = new RegisteredStrategy(strategy, fingerprint);
            Pipeline = pipeline ?? new FakeStrategyPipeline();
            Time = new FakeTimeProvider(Now);
            Reservation = _request.ToDomain();
            RequestHash = RequestHasher.ComputeHash(_request);
            Evaluator = new StrategyEvaluator(Pipeline, Cache, Time, Logger);
        }

        public RegisteredStrategy Registered { get; }

        public FakeStrategyPipeline Pipeline { get; }

        public FakeStrategyDecisionCache Cache { get; } = new();

        public FakeTimeProvider Time { get; }

        public CapturingLogger<StrategyEvaluator> Logger { get; } = new();

        public Reservation Reservation { get; }

        public string RequestHash { get; }

        public string CacheKey => CacheKeyOf(Registered, RequestHash);

        public StrategyEvaluator Evaluator { get; }

        public Task<StrategyEvaluation> EvaluateAsync(CancellationToken cancellationToken = default) =>
            Evaluator.EvaluateAsync(
                Registered,
                Reservation,
                new DecisionContext(Now, DomainTestData.StandardCalendar()),
                RequestHash,
                IdempotencyKey,
                cancellationToken);
    }

    /// <summary>Cache có sẵn kết quả: trả <c>CACHE_HIT</c>, đánh dấu <c>CacheHit</c> và không gọi pipeline hay ghi lại cache.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_CacheHit_ReturnsCachedWithoutPipeline()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        var cached = StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Cọc 50%."]);
        fixture.Cache.Seed(fixture.CacheKey, cached);

        var evaluation = await fixture.EvaluateAsync();

        evaluation.CacheHit.Should().BeTrue();
        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        evaluation.Outcome.FallbackUsed.Should().BeFalse();
        evaluation.Outcome.FailureReason.Should().BeNull();
        evaluation.Outcome.Decision.Should().BeSameAs(cached);
        fixture.Pipeline.InvocationCount.Should().Be(0);
        fixture.Cache.Sets.Should().BeEmpty("kết quả đọc từ cache không được ghi lại");
    }

    /// <summary>Thời gian của <c>CACHE_HIT</c> là thời gian đọc cache đo bằng <see cref="TimeProvider"/>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheHit_MeasuresCacheReadTimeWithTimeProvider()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        fixture.Cache.Seed(fixture.CacheKey, StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Cọc 50%."]));
        fixture.Cache.OnGet = () => fixture.Time.Advance(TimeSpan.FromMilliseconds(7));

        var evaluation = await fixture.EvaluateAsync();

        evaluation.Outcome.ExecutionTimeMs.Should().Be(7);
    }

    /// <summary>Miss và kết quả <c>AUTOMATED</c>: chạy pipeline một lần rồi ghi cache đúng khóa, đúng TTL của chính sách.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheMissAutomated_WritesCacheWithPolicyTtl()
    {
        var ttl = TimeSpan.FromMinutes(10);
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(ttl)));

        var evaluation = await fixture.EvaluateAsync();

        evaluation.CacheHit.Should().BeFalse();
        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        fixture.Cache.GetKeys.Should().Equal(fixture.CacheKey);
        var written = fixture.Cache.Sets.Should().ContainSingle().Subject;
        written.Key.Should().Be(fixture.CacheKey);
        written.Ttl.Should().Be(ttl);
        written.Decision.Status.Should().Be(DecisionStatus.APPROVED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    /// <summary>Pipeline trả <c>FALLBACK</c> (kết quả tạm thời) thì không ghi cache.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_FallbackOutcome_IsNotCached()
    {
        var pipeline = FakeStrategyPipeline.Fallback(
            FallbackDecisionPolicy.Create(FallbackCause.Timeout),
            $"{FailureReasonPrefixes.StrategyTimeout}: vượt 400 ms");
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), pipeline);

        var evaluation = await fixture.EvaluateAsync();

        evaluation.CacheHit.Should().BeFalse();
        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        evaluation.Outcome.FallbackUsed.Should().BeTrue();
        fixture.Cache.Sets.Should().BeEmpty("chỉ kết quả AUTOMATED mới được cache");
    }

    /// <summary>Chính sách cache <c>Disabled</c>: không dựng khóa, không đọc và không ghi cache; pipeline vẫn chạy.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CachePolicyDisabled_NeverTouchesCache()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Disabled));

        var evaluation = await fixture.EvaluateAsync();

        evaluation.CacheHit.Should().BeFalse();
        fixture.Cache.GetKeys.Should().BeEmpty();
        fixture.Cache.Sets.Should().BeEmpty();
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    /// <summary>Tên giữ từ <c>DecisionServiceTests</c> (kịch bản tương đương <see cref="EvaluateAsync_CachePolicyDisabled_NeverTouchesCache"/>).</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheDisabledPolicy_NeverTouchesCache()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Disabled));

        await fixture.EvaluateAsync();

        fixture.Cache.GetKeys.Should().BeEmpty();
        fixture.Cache.Sets.Should().BeEmpty();
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    /// <summary>Đọc và ghi cache đều ném: coi như miss, pipeline chạy, kết quả <c>AUTOMATED</c>, mỗi lỗi một cảnh báo kèm idempotency key.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheThrows_TreatedAsMiss()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        fixture.Cache.FailGets(new InvalidOperationException("Redis chết khi đọc."));
        fixture.Cache.FailSets(new InvalidOperationException("Redis chết khi ghi."));

        var evaluation = await fixture.EvaluateAsync();

        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        evaluation.Outcome.Decision.Status.Should().Be(DecisionStatus.APPROVED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
        var warnings = fixture.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2, "một cảnh báo cho đọc và một cho ghi");
        warnings.Should().OnlyContain(entry => entry.Message.Contains(IdempotencyKey, StringComparison.Ordinal));
    }

    /// <summary>Từng tổ hợp lỗi đọc/ghi cache: không bao giờ làm thất bại lần đánh giá và mỗi lỗi để lại đúng một cảnh báo.</summary>
    [Theory]
    [Trait("CatalogId", "UT-13")]
    [MemberData(nameof(CacheFailures))]
    public async Task EvaluateAsync_CacheReadOrWriteThrows_TreatedAsMiss_Theory(bool failRead, bool failWrite)
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        if (failRead)
        {
            fixture.Cache.FailGets(new InvalidOperationException("Redis chết khi đọc."));
        }

        if (failWrite)
        {
            fixture.Cache.FailSets(new TimeoutException("Redis chết khi ghi."));
        }

        var evaluation = await fixture.EvaluateAsync();

        evaluation.CacheHit.Should().BeFalse();
        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
        fixture.Logger.Entries.Count(entry => entry.Level == LogLevel.Warning).Should().Be((failRead ? 1 : 0) + (failWrite ? 1 : 0));
    }

    /// <summary>Hủy không phải của caller (timeout nội bộ của Redis) cũng chỉ là miss.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheThrowsCancellationNotFromCaller_TreatedAsMiss()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        fixture.Cache.FailGets(new TaskCanceledException("Timeout nội bộ của Redis."));
        fixture.Cache.FailSets(new OperationCanceledException("Timeout nội bộ của Redis."));

        var evaluation = await fixture.EvaluateAsync();

        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    /// <summary>Caller hủy giữa lúc đọc cache: <see cref="OperationCanceledException"/> lan ra, pipeline không chạy.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheCancelledByCaller_Propagates()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        using var cts = new CancellationTokenSource();
        fixture.Cache.OnGet = cts.Cancel;
        fixture.Cache.FailGets(new OperationCanceledException(cts.Token));

        var act = () => fixture.EvaluateAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Pipeline.InvocationCount.Should().Be(0);
    }

    /// <summary>Token đã bị hủy trước khi đọc cache: lỗi hủy của chính caller không bị nuốt thành miss.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_CallerCancelled_Propagates()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        fixture.Cache.FailGets(new OperationCanceledException(cts.Token));

        var act = () => fixture.EvaluateAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Pipeline.InvocationCount.Should().Be(0);
        fixture.Cache.Sets.Should().BeEmpty();
    }

    /// <summary>Không dựng được khóa cache (dấu vân tay rỗng): bỏ qua cache kèm cảnh báo, pipeline vẫn chạy.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheKeyCannotBeBuilt_SkipsCacheAndWarns()
    {
        var fixture = new Fixture(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), fingerprint: " ");

        var evaluation = await fixture.EvaluateAsync();

        evaluation.Outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        fixture.Cache.GetKeys.Should().BeEmpty();
        fixture.Cache.Sets.Should().BeEmpty();
        fixture.Pipeline.InvocationCount.Should().Be(1);
        fixture.Logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(IdempotencyKey, StringComparison.Ordinal));
    }
}
