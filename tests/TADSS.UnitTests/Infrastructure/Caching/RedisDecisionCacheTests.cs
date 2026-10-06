using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using StackExchange.Redis;
using TADSS.Infrastructure.Caching;
using TADSS.Infrastructure.Options;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// UT-13: tầng Redis của cache (<see cref="RedisDecisionCache"/>) — mọi lệnh Redis chạy trong pipeline timeout + circuit breaker:
/// lỗi và quá hạn được breaker đếm, breaker mở thì bỏ qua Redis ngay (không gọi, không chờ timeout), thăm dò lại sau thời gian mở,
/// payload hỏng chỉ là miss và không làm breaker mở. Redis được mô phỏng bằng Moq, đồng hồ bằng <see cref="FakeTimeProvider"/>.
/// </summary>
public sealed class RedisDecisionCacheTests
{
    private const string FullKey = "tadss:v2:Default:2.0.0:a1b2c3d4e5f60718:9f86d081884c7d659a2feaa0c55ad015";

    private readonly Mock<IDatabase> _database = new();
    private readonly FakeTimeProvider _time = new();
    private readonly CapturingLogger<StrategyDecisionCache> _logger = new();

    private static readonly StrategyDecision Decision = StrategyDecision.Approve(0.3m, "OFF_PEAK_DEPOSIT", ["Giờ thấp điểm"]);

    private RedisDecisionCache CreateCache(int timeoutMs = 150, int minimumThroughput = 2, int breakSeconds = 30)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);
        var options = new CacheOptions
        {
            RedisTimeoutMs = timeoutMs,
            RedisBreakerMinimumThroughput = minimumThroughput,
            RedisBreakerBreakDurationSeconds = breakSeconds,
        };
        return new RedisDecisionCache(multiplexer.Object, options, _time, _logger);
    }

    private void SetupGet(Func<RedisValue> result) =>
        _database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(() => Task.FromResult(result()));

    private void SetupGetThrows() =>
        _database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis không kết nối được"));

    private void VerifyGetCalls(int times) =>
        _database.Verify(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Exactly(times));

    private static RedisValue ValidPayload() =>
        JsonSerializer.Serialize(CachedStrategyDecision.From(Decision), TadssJson.Options);

    /// <summary>Redis lỗi liên tục tới ngưỡng thì breaker mở: các lần sau trả Bypassed và không gọi Redis.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task GetAsync_RedisFailuresReachThreshold_OpensBreakerAndBypasses()
    {
        var cache = CreateCache();
        SetupGetThrows();

        var first = await cache.GetAsync(FullKey, CancellationToken.None);
        var second = await cache.GetAsync(FullKey, CancellationToken.None);
        var third = await cache.GetAsync(FullKey, CancellationToken.None);

        first.Status.Should().Be(RedisCacheStatus.Failed);
        second.Status.Should().Be(RedisCacheStatus.Failed);
        third.Status.Should().Be(RedisCacheStatus.Bypassed);
        VerifyGetCalls(2);
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("breaker Redis MỞ"));
    }

    /// <summary>Redis không bao giờ trả lời: sau <c>RedisTimeoutMs</c> lệnh bị cắt và trả Failed (không treo).</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task GetAsync_SlowRedis_TimesOutWithinRedisTimeout()
    {
        var cache = CreateCache(timeoutMs: 50);
        var started = false;
        _database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(() =>
            {
                started = true;
                return new TaskCompletionSource<RedisValue>().Task;
            });

        var pending = cache.GetAsync(FullKey, CancellationToken.None);
        await WaitUntilAsync(() => started);
        _time.Advance(TimeSpan.FromMilliseconds(60));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        result.Status.Should().Be(RedisCacheStatus.Failed);
    }

    /// <summary>Quá hạn (không phải exception của Redis) cũng được breaker đếm là lỗi: hai lệnh quá hạn liên tiếp làm breaker mở, lệnh sau bỏ qua Redis mà không chờ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task GetAsync_TimeoutsReachThreshold_OpensBreakerAndBypasses()
    {
        var cache = CreateCache(timeoutMs: 50);
        var calls = 0;
        _database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref calls);
                return new TaskCompletionSource<RedisValue>().Task;
            });

        for (var expectedCalls = 1; expectedCalls <= 2; expectedCalls++)
        {
            var pending = cache.GetAsync(FullKey, CancellationToken.None);
            await WaitUntilAsync(() => Volatile.Read(ref calls) == expectedCalls);
            _time.Advance(TimeSpan.FromMilliseconds(60));
            (await pending.WaitAsync(TimeSpan.FromSeconds(10))).Status.Should().Be(RedisCacheStatus.Failed);
        }

        var third = await cache.GetAsync(FullKey, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        third.Status.Should().Be(RedisCacheStatus.Bypassed, "hai lần quá hạn đủ ngưỡng để breaker mở");
        Volatile.Read(ref calls).Should().Be(2, "breaker mở thì không gọi Redis và không chờ timeout");
    }

    /// <summary>Hết thời gian mở: lần thăm dò gọi Redis, thành công thì breaker đóng và các lần sau dùng Redis bình thường.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task GetAsync_AfterBreakDuration_HalfOpenThenClosesOnSuccess()
    {
        var cache = CreateCache(breakSeconds: 5);
        SetupGetThrows();
        await cache.GetAsync(FullKey, CancellationToken.None);
        await cache.GetAsync(FullKey, CancellationToken.None);
        (await cache.GetAsync(FullKey, CancellationToken.None)).Status.Should().Be(RedisCacheStatus.Bypassed);

        SetupGet(ValidPayload);
        _time.Advance(TimeSpan.FromSeconds(6));
        var probe = await cache.GetAsync(FullKey, CancellationToken.None);
        var afterClose = await cache.GetAsync(FullKey, CancellationToken.None);

        probe.Status.Should().Be(RedisCacheStatus.Hit);
        afterClose.Status.Should().Be(RedisCacheStatus.Hit);
        _logger.Entries.Should().Contain(entry => entry.Message.Contains("breaker Redis ĐÓNG"));
    }

    /// <summary>Payload hỏng nhiều lần là miss và không bao giờ làm breaker mở (Redis vẫn khỏe).</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task GetAsync_CorruptPayload_IsMissAndDoesNotTripBreaker()
    {
        var cache = CreateCache();
        SetupGet(() => "{not json");

        for (var i = 0; i < 6; i++)
        {
            (await cache.GetAsync(FullKey, CancellationToken.None)).Status.Should().Be(RedisCacheStatus.Miss);
        }

        VerifyGetCalls(6);
    }

    /// <summary>Khi breaker mở, ghi cũng bị bỏ qua mà không gọi Redis.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task SetAsync_BreakerOpen_BypassesWithoutCallingRedis()
    {
        var cache = CreateCache();
        SetupGetThrows();
        await cache.GetAsync(FullKey, CancellationToken.None);
        await cache.GetAsync(FullKey, CancellationToken.None);

        var status = await cache.SetAsync(FullKey, Decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        status.Should().Be(RedisCacheStatus.Bypassed);
        _database.Verify(
            db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    /// <summary>Hủy của chính caller lan ra và không bị tính là lỗi của Redis.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task GetAsync_CallerCancelled_ThrowsAndDoesNotTripBreaker()
    {
        var cache = CreateCache();
        SetupGet(() => RedisValue.Null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        for (var i = 0; i < 4; i++)
        {
            var act = async () => await cache.GetAsync(FullKey, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        (await cache.GetAsync(FullKey, CancellationToken.None)).Status.Should().Be(RedisCacheStatus.Miss);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Điều kiện không đạt trong 10 giây.");
            }

            await Task.Delay(5);
        }
    }
}
