using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.V2.Configuration;
using TADSS.Runtime.Api.V2.Infrastructure.Caching;
using TADSS.Runtime.Api.V2.Infrastructure.Data;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Middlewares;
using TADSS.Runtime.Api.V2.Services;
using TADSS.Runtime.Api.V2.Services.Decisions;
using TADSS.Runtime.Api.V2.Services.Strategies;
using TADSS.UnitTests.V2.Runtime.TestDoubles;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Services;

/// <summary>
/// UT-11/UT-13: <see cref="DecisionService"/> điều phối đúng thứ tự idempotency → domain → cache → pipeline → approval
/// window → audit (fail-closed, INV-10), replay nguyên văn khi trùng key và hash, báo xung đột khi khác hash (kể cả khi
/// thua tranh chấp), tự cô lập lỗi cache, lấy thời gian từ <see cref="TimeProvider"/> và không ghi gì khi lỗi xảy ra
/// trước bước ghi audit. Store, cache, pipeline và registry là test double viết tay; quyết định thật lấy từ strategy v2.
/// </summary>
public class DecisionServiceTests
{
    private const string IdempotencyKey = "idem-key-0001";
    private const string CorrelationId = "corr-0001";
    private const string StrategyFingerprint = "0123456789abcdef";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 2, 15, 0, TimeSpan.Zero);

    public static TheoryData<string> InvalidKeys => new()
    {
        string.Empty,
        "   ",
        new string('k', 129)
    };

    private static DecisionRequestDto Request(Action<JsonObject>? mutate = null) =>
        V2Samples.DeserializeRequest(mutate is null ? V2Samples.RequestJson : V2Samples.MutateRequest(mutate));

    private static RegisteredStrategy DefaultStrategy() =>
        StrategyRegistrations.CreateDefault(new ConfigurationBuilder().Build(), V2TestData.StandardCalendar());

    private static DelegateStrategy ApproveStrategy(StrategyCachePolicy? cachePolicy = null) =>
        new(
            (_, _, _) => Task.FromResult(StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Khung giờ cao điểm áp dụng cọc 50%."])),
            cachePolicy);

    private static DelegateStrategy NeedApprovalStrategy(StrategyCachePolicy? cachePolicy = null) =>
        new(
            (_, _, _) => Task.FromResult(StrategyDecision.RequireApproval(
                ReasonCodes.HighValueReview,
                ["Đơn giá trị lớn cần Manager duyệt."],
                [new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Cần Manager duyệt.")])),
            cachePolicy);

    private static string CacheKeyOf(RegisteredStrategy strategy, DecisionRequestDto request) =>
        StrategyDecisionCacheKey.Build(
            strategy.Strategy.StrategyName,
            strategy.Strategy.StrategyVersion,
            strategy.OptionsFingerprint,
            RequestHasher.ComputeHash(request));

    private static DecisionResultDto Deserialize(string payload) =>
        V2Samples.Required(JsonSerializer.Deserialize<DecisionResultDto>(payload, TadssJson.Options));

    /// <summary>Bộ phụ thuộc giả dùng chung cho một lần dựng <see cref="DecisionService"/>.</summary>
    private sealed class Fixture
    {
        public Fixture(
            RegisteredStrategy? strategy = null,
            FakeStrategyPipeline? pipeline = null,
            FakeStrategyRegistry? registry = null,
            FakeDecisionAuditStore? store = null,
            DateTimeOffset? now = null)
        {
            Strategy = strategy ?? DefaultStrategy();
            Time = new FakeTimeProvider(now ?? Now);
            Pipeline = pipeline ?? new FakeStrategyPipeline();
            Registry = registry ?? new FakeStrategyRegistry(Strategy);
            Store = store ?? new FakeDecisionAuditStore();

            Service = new DecisionService(
                Store,
                Registry,
                Pipeline,
                Cache,
                new ItemDurationPolicy(Options.Create(new ValidationOptions())),
                new DecisionResponseComposer(
                    new DepositCalculator(),
                    new ApprovalWindowPolicy(Options.Create(new ApprovalOptions()))),
                V2TestData.StandardCalendar(),
                Time,
                Logger);
        }

        public RegisteredStrategy Strategy { get; }

        public FakeTimeProvider Time { get; }

        public FakeStrategyPipeline Pipeline { get; }

        public FakeStrategyRegistry Registry { get; }

        public FakeDecisionAuditStore Store { get; }

        public FakeStrategyDecisionCache Cache { get; } = new();

        public CapturingLogger<DecisionService> Logger { get; } = new();

        public DecisionService Service { get; }

        public Task<DecisionResultDto> EvaluateAsync(
            DecisionRequestDto request,
            string idempotencyKey = IdempotencyKey,
            string? correlationId = CorrelationId,
            CancellationToken cancellationToken = default) =>
            Service.EvaluateAsync(request, idempotencyKey, correlationId, cancellationToken);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_NewRequest_PersistsAndReturnsAutomated()
    {
        var fixture = new Fixture();

        var response = await fixture.EvaluateAsync(Request());

        response.Status.Should().Be(DecisionStatus.APPROVED);
        V2Samples.Required(response.Deposit).Rate.Should().Be(0.3m);
        response.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        var record = fixture.Store.Records.Should().ContainSingle().Subject;
        record.Role.Should().Be(ExecutionRole.PRIMARY);
        Deserialize(record.ResponsePayload).Should().BeEquivalentTo(response);
        record.DecisionId.Should().Be(response.DecisionId);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_SameKeySameHash_ReplaysStoredResponse()
    {
        var fixture = new Fixture();
        var request = Request();

        var first = await fixture.EvaluateAsync(request);
        var second = await fixture.EvaluateAsync(Request());

        second.Should().BeEquivalentTo(first);
        second.DecisionId.Should().Be(first.DecisionId);
        fixture.Pipeline.InvocationCount.Should().Be(1);
        fixture.Registry.ResolveCount.Should().Be(1);
        fixture.Store.Records.Should().HaveCount(1);
        fixture.Store.InsertCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_SameKeyOnlyDemandChanged_Replays()
    {
        var fixture = new Fixture();
        var first = await fixture.EvaluateAsync(Request());

        var changed = Request(root =>
        {
            root["demand"] = new JsonObject { ["occupancyRate"] = 0.95, ["waitlistCount"] = 9 };
            root["requestedAt"] = "2026-11-18T10:00:00+07:00";
            root["metadata"] = new JsonObject { ["promotionCode"] = "SUMMER" };
        });
        var second = await fixture.EvaluateAsync(changed);

        second.DecisionId.Should().Be(first.DecisionId);
        fixture.Pipeline.InvocationCount.Should().Be(1);
        fixture.Store.InsertCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_SameKeyDifferentHash_ThrowsConflict()
    {
        var fixture = new Fixture();
        await fixture.EvaluateAsync(Request());

        var act = () => fixture.EvaluateAsync(Request(root => root["totalPrice"] = 200000));

        var exception = (await act.Should().ThrowAsync<IdempotencyConflictException>()).Which;
        exception.IdempotencyKey.Should().Be(IdempotencyKey);
        fixture.Store.InsertCount.Should().Be(1, "request xung đột không được ghi audit");
        fixture.Pipeline.InvocationCount.Should().Be(1, "request xung đột không được chạy strategy");
        fixture.Registry.ResolveCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_IdempotencyCheckedBeforeStrategyResolution()
    {
        var original = new Fixture();
        var first = await original.EvaluateAsync(Request());
        var store = new FakeDecisionAuditStore();
        store.Seed(original.Store.Records.Single());
        var fixture = new Fixture(registry: new FakeStrategyRegistry(), store: store);

        var replayed = await fixture.EvaluateAsync(Request());

        replayed.DecisionId.Should().Be(first.DecisionId);
        fixture.Registry.ResolveCount.Should().Be(0);
        fixture.Pipeline.InvocationCount.Should().Be(0);
        fixture.Store.InsertCount.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_ItemTooShort_ThrowsBeforeStrategy()
    {
        var fixture = new Fixture();
        var request = Request(root =>
        {
            var item = V2Samples.Object(root["items"]?.AsArray()[0]);
            item["endTime"] = "2026-11-20T18:15:00+07:00";
        });

        var act = () => fixture.EvaluateAsync(request);

        var exception = (await act.Should().ThrowAsync<DomainInvariantException>()).Which;
        exception.InvariantCode.Should().Be("INVALID_ITEM_DURATION");
        fixture.Store.InsertCount.Should().Be(0);
        fixture.Registry.ResolveCount.Should().Be(0);
        fixture.Pipeline.InvocationCount.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_FormatViolation_ThrowsContractValidation()
    {
        var fixture = new Fixture();
        var request = Request(root => root["customer"] = null);

        var act = () => fixture.EvaluateAsync(request);

        await act.Should().ThrowAsync<ContractValidationException>();
        fixture.Store.InsertCount.Should().Be(0);
        fixture.Registry.ResolveCount.Should().Be(0);
        fixture.Pipeline.InvocationCount.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheEnabledHit_ReturnsCacheHitWithNewIdentity()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        var request = Request();
        fixture.Cache.Seed(CacheKeyOf(strategy, request), StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Cọc 50%."]));

        var first = await fixture.EvaluateAsync(request, "idem-key-0001");
        var second = await fixture.EvaluateAsync(request, "idem-key-0002");

        first.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        second.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        first.DecisionId.Should().NotBe(second.DecisionId);
        fixture.Pipeline.InvocationCount.Should().Be(0);
        fixture.Cache.Sets.Should().BeEmpty("kết quả đọc từ cache không được ghi lại");
        fixture.Store.Records.Should().HaveCount(2).And.OnlyContain(record => record.CacheHit && record.ExecutionType == ExecutionType.CACHE_HIT);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheHit_RecordsMeasuredCacheReadTime()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        var request = Request();
        fixture.Cache.Seed(CacheKeyOf(strategy, request), StrategyDecision.Approve(0.5m, ReasonCodes.PeakHourDeposit, ["Cọc 50%."]));
        fixture.Cache.OnGet = () => fixture.Time.Advance(TimeSpan.FromMilliseconds(7));

        var response = await fixture.EvaluateAsync(request);

        // Thời gian là thời gian đọc cache đo bằng TimeProvider; evaluatedAt vẫn là mốc nowUtc lấy trước khi đọc
        fixture.Store.Records.Should().ContainSingle().Which.ExecutionTimeMs.Should().Be(7);
        response.EvaluatedAt.Should().Be(Now);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_CacheHit_RecomputesApprovalWindow()
    {
        var strategy = new RegisteredStrategy(NeedApprovalStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        var request = Request();
        fixture.Cache.Seed(
            CacheKeyOf(strategy, request),
            StrategyDecision.RequireApproval(ReasonCodes.HighValueReview, ["Đơn giá trị lớn cần Manager duyệt."], []));

        var first = await fixture.EvaluateAsync(request, "idem-key-0001");
        fixture.Time.Advance(TimeSpan.FromHours(1));
        var second = await fixture.EvaluateAsync(request, "idem-key-0002");

        first.ExecutionType.Should().Be(ExecutionType.CACHE_HIT);
        first.ApprovalExpiresAt.Should().Be(Now.AddMinutes(30));
        second.ApprovalExpiresAt.Should().Be(Now.AddHours(1).AddMinutes(30));
        second.EvaluatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheMissAutomated_WritesCacheWithPolicyTtl()
    {
        var ttl = TimeSpan.FromMinutes(10);
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(ttl)), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        var request = Request();

        var response = await fixture.EvaluateAsync(request);

        response.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        var expectedKey = CacheKeyOf(strategy, request);
        fixture.Cache.GetKeys.Should().Equal(expectedKey);
        var written = fixture.Cache.Sets.Should().ContainSingle().Subject;
        written.Key.Should().Be(expectedKey);
        written.Ttl.Should().Be(ttl);
        written.Decision.Status.Should().Be(DecisionStatus.APPROVED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_FallbackOutcome_NotCached()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var pipeline = FakeStrategyPipeline.Fallback(
            FallbackDecisionPolicy.Create(FallbackCause.Timeout),
            $"{FailureReasonPrefixes.StrategyTimeout}: vượt 400 ms");
        var fixture = new Fixture(strategy, pipeline);

        var response = await fixture.EvaluateAsync(Request());

        fixture.Cache.Sets.Should().BeEmpty("chỉ kết quả AUTOMATED mới được cache");
        response.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        response.ApprovalExpiresAt.Should().NotBeNull();
        response.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        response.FallbackUsed.Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheDisabledPolicy_NeverTouchesCache()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Disabled), StrategyFingerprint);
        var fixture = new Fixture(strategy);

        await fixture.EvaluateAsync(Request());

        fixture.Cache.GetKeys.Should().BeEmpty();
        fixture.Cache.Sets.Should().BeEmpty();
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheThrows_TreatedAsMiss()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        fixture.Cache.FailGets(new InvalidOperationException("Redis chết khi đọc."));
        fixture.Cache.FailSets(new InvalidOperationException("Redis chết khi ghi."));

        var response = await fixture.EvaluateAsync(Request());

        response.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        response.Status.Should().Be(DecisionStatus.APPROVED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
        fixture.Store.Records.Should().ContainSingle();
        var warnings = fixture.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2, "một cảnh báo cho đọc và một cho ghi");
        warnings.Should().OnlyContain(entry => entry.Message.Contains(IdempotencyKey, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheThrowsCancellationNotFromCaller_TreatedAsMiss()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        fixture.Cache.FailGets(new TaskCanceledException("Timeout nội bộ của Redis."));
        fixture.Cache.FailSets(new OperationCanceledException("Timeout nội bộ của Redis."));

        var response = await fixture.EvaluateAsync(Request());

        response.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        fixture.Pipeline.InvocationCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task EvaluateAsync_CacheCancelledByCaller_Propagates()
    {
        var strategy = new RegisteredStrategy(ApproveStrategy(StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10))), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        using var cts = new CancellationTokenSource();
        fixture.Cache.OnGet = cts.Cancel;
        fixture.Cache.FailGets(new OperationCanceledException(cts.Token));

        var act = () => fixture.EvaluateAsync(Request(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Pipeline.InvocationCount.Should().Be(0);
        fixture.Store.InsertCount.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_WindowUnavailable_LogsStrategyReasonCode()
    {
        var strategy = new RegisteredStrategy(NeedApprovalStrategy(), StrategyFingerprint);
        var fixture = new Fixture(strategy);
        // Giờ bắt đầu cách now 1 giờ: hạn duyệt (start - 120 phút) đã ở quá khứ
        var request = Request(root =>
        {
            var item = V2Samples.Object(root["items"]?.AsArray()[0]);
            item["startTime"] = "2026-11-18T10:15:00+07:00";
            item["endTime"] = "2026-11-18T12:15:00+07:00";
        });

        var response = await fixture.EvaluateAsync(request);

        response.ReasonCode.Should().Be(ReasonCodes.ApprovalWindowUnavailable);
        response.Status.Should().Be(DecisionStatus.APPROVED);
        V2Samples.Required(response.Deposit).Rate.Should().Be(1m);
        var original = fixture.Logger.Entries.Should()
            .ContainSingle(entry => entry.Level == LogLevel.Information
                && entry.Message.Contains(ReasonCodes.HighValueReview, StringComparison.Ordinal)
                && entry.Message.Contains("approval window", StringComparison.OrdinalIgnoreCase))
            .Subject;
        original.Message.Should().Contain(IdempotencyKey);
        fixture.Store.Records.Should().ContainSingle().Which.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_FinalDecision_LogsStructuredSummaryWithoutPayload()
    {
        var fixture = new Fixture();

        var response = await fixture.EvaluateAsync(Request());

        var entry = fixture.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information).Subject;
        entry.Message.Should().Contain(response.DecisionId.ToString())
            .And.Contain(IdempotencyKey)
            .And.Contain(CorrelationId)
            .And.Contain("DefaultDecisionStrategy")
            .And.Contain("AUTOMATED")
            .And.Contain("APPROVED");
        entry.Message.Should().NotContain("9d3f1c2a-0000-4000-8000-000000000123", "không được log payload/dữ liệu khách hàng");
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_StoreUnavailable_ThrowsAndReturnsNothing()
    {
        var store = new FakeDecisionAuditStore();
        store.FailInsertWithPersistenceUnavailable();
        var fixture = new Fixture(store: store);

        var act = () => fixture.EvaluateAsync(Request());

        await act.Should().ThrowAsync<PersistenceUnavailableException>();
        fixture.Store.Records.Should().BeEmpty();
        fixture.Store.InsertCount.Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_LostRaceSameHash_ReturnsWinner()
    {
        var original = new Fixture();
        var winnerResponse = await original.EvaluateAsync(Request());
        var store = new FakeDecisionAuditStore();
        store.SimulateLostRace(original.Store.Records.Single());
        var fixture = new Fixture(store: store);

        var response = await fixture.EvaluateAsync(Request());

        response.DecisionId.Should().Be(winnerResponse.DecisionId);
        response.Should().BeEquivalentTo(winnerResponse);
        fixture.Pipeline.InvocationCount.Should().Be(1, "request thua tranh chấp đã chạy strategy trước khi ghi");
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_LostRaceDifferentHash_ThrowsConflict()
    {
        var original = new Fixture();
        await original.EvaluateAsync(Request());
        var winner = original.Store.Records.Single() with { RequestHash = new string('a', 64) };
        var store = new FakeDecisionAuditStore();
        store.SimulateLostRace(winner);
        var fixture = new Fixture(store: store);

        var act = () => fixture.EvaluateAsync(Request());

        var exception = (await act.Should().ThrowAsync<IdempotencyConflictException>()).Which;
        exception.IdempotencyKey.Should().Be(IdempotencyKey);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_ContextUsesTimeProvider()
    {
        var evaluatedAt = new DateTimeOffset(2030, 5, 1, 3, 0, 0, TimeSpan.Zero);
        var strategy = ApproveStrategy();
        var fixture = new Fixture(new RegisteredStrategy(strategy, StrategyFingerprint), now: evaluatedAt);

        var response = await fixture.EvaluateAsync(Request());

        V2Samples.Required(strategy.LastContext).EvaluatedAtUtc.Should().Be(evaluatedAt);
        fixture.Pipeline.LastContext.Should().BeSameAs(strategy.LastContext);
        response.EvaluatedAt.Should().Be(evaluatedAt);
        fixture.Store.Records.Should().ContainSingle().Which.CreatedAt.Should().Be(evaluatedAt);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_NowWithSubMicrosecondTicks_IsTruncatedConsistently()
    {
        // 123_456_789 tick lẻ 9 tick (0,9 µs): giá trị cắt phải là bội số của 10 tick và dùng nhất quán ở mọi nơi (B-03)
        var rawNow = Now.AddTicks(123_456_789);
        var truncatedNow = Now.AddTicks(123_456_780);
        var strategy = ApproveStrategy();
        var fixture = new Fixture(new RegisteredStrategy(strategy, StrategyFingerprint), now: rawNow);

        var response = await fixture.EvaluateAsync(Request());

        V2Samples.Required(strategy.LastContext).EvaluatedAtUtc.Should().Be(truncatedNow);
        response.EvaluatedAt.Should().Be(truncatedNow);
        var record = fixture.Store.Records.Should().ContainSingle().Subject;
        record.CreatedAt.Should().Be(truncatedNow);
        (record.CreatedAt.UtcTicks % 10).Should().Be(0);
        Deserialize(record.ResponsePayload).EvaluatedAt.Should().Be(truncatedNow);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_NeedApprovalWithSubMicrosecondTicks_ApprovalWindowUsesTruncatedNow()
    {
        var rawNow = Now.AddTicks(123_456_789);
        var truncatedNow = Now.AddTicks(123_456_780);
        var strategy = new RegisteredStrategy(NeedApprovalStrategy(), StrategyFingerprint);
        var fixture = new Fixture(strategy, now: rawNow);

        var response = await fixture.EvaluateAsync(Request());

        response.ApprovalExpiresAt.Should().Be(truncatedNow.AddMinutes(30));
        fixture.Store.Records.Should().ContainSingle().Which.ApprovalExpiresAt.Should().Be(truncatedNow.AddMinutes(30));
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public async Task EvaluateAsync_ApprovedWithRateScaleOverFour_ThrowsWithoutPersisting()
    {
        var strategy = new DelegateStrategy(
            (_, _, _) => Task.FromResult(StrategyDecision.Approve(0.12345m, ReasonCodes.PeakHourDeposit, ["Tỷ lệ lẻ."])));
        var fixture = new Fixture(new RegisteredStrategy(strategy, StrategyFingerprint));

        var act = () => fixture.EvaluateAsync(Request());

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.InsertCount.Should().Be(0);
        fixture.Store.Records.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_PersistedRecord_MatchesSpecColumns()
    {
        var fixture = new Fixture();
        var request = Request();

        var response = await fixture.EvaluateAsync(request);

        var record = fixture.Store.Records.Should().ContainSingle().Subject;
        record.Role.Should().Be(ExecutionRole.PRIMARY);
        record.BranchId.Should().Be(request.BranchId);
        record.ReservationId.Should().Be(request.ReservationId);
        record.RequestHash.Should().Be(RequestHasher.ComputeHash(request));
        record.CorrelationId.Should().Be(CorrelationId);
        record.IdempotencyKey.Should().Be(IdempotencyKey);
        record.CreatedAt.Should().Be(Now);
        record.DecisionId.Should().Be(response.DecisionId);
        record.StrategyName.Should().Be("DefaultDecisionStrategy");
        record.CacheHit.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_NullCorrelationId_PersistsNull()
    {
        var fixture = new Fixture();

        await fixture.EvaluateAsync(Request(), correlationId: null);

        fixture.Store.Records.Should().ContainSingle().Which.CorrelationId.Should().BeNull();
    }

    [Theory]
    [Trait("CatalogId", "UT-11")]
    [MemberData(nameof(InvalidKeys))]
    public async Task EvaluateAsync_InvalidIdempotencyKey_Throws_Theory(string key)
    {
        var fixture = new Fixture();

        var act = () => fixture.EvaluateAsync(Request(), key);

        await act.Should().ThrowAsync<ArgumentException>();
        fixture.Store.FindCount.Should().Be(0);
        fixture.Store.InsertCount.Should().Be(0);
        fixture.Registry.ResolveCount.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_MaxLengthIdempotencyKey_Accepted()
    {
        var fixture = new Fixture();

        var response = await fixture.EvaluateAsync(Request(), new string('k', 128));

        response.Status.Should().Be(DecisionStatus.APPROVED);
    }

    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task EvaluateAsync_CallerCancelled_PropagatesWithoutPersisting()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = new FakeStrategyPipeline((_, _, _, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromException<StrategyExecutionOutcome>(new InvalidOperationException("Không tới được đây."));
        });
        var fixture = new Fixture(pipeline: pipeline);

        var act = () => fixture.EvaluateAsync(Request(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.InsertCount.Should().Be(0);
        fixture.Store.Records.Should().BeEmpty();
    }
}
