using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using StackExchange.Redis;
using TADSS.Infrastructure.Caching;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// UT-13: cache hai tầng của <see cref="StrategyDecision"/> (<see cref="StrategyDecisionCache"/>) — chỉ cache quyết định
/// của strategy (không phải response), memory giữ chính instance bất biến, Redis lưu JSON theo <c>TadssJson</c> và dựng
/// lại qua factory đúng <c>Status</c>; mọi lỗi cache (Redis, memory, payload hỏng) là miss/bỏ qua, chỉ
/// <see cref="OperationCanceledException"/> của caller được lan ra. Redis được mô phỏng bằng Moq (Redis thật ở IT-13).
/// </summary>
public sealed class StrategyDecisionCacheTests : IDisposable
{
    private const string Key = "tadss:v2:Default:2.0.0:a1b2c3d4e5f60718:9f86d081884c7d659a2feaa0c55ad015";

    private const string ValidTail = "\"reasonCode\":\"OFF_PEAK_DEPOSIT\",\"reasons\":[\"a\"],\"recommendations\":[]";

    private readonly MemoryCache _memory = new(new MemoryCacheOptions());
    private readonly CapturingLogger<StrategyDecisionCache> _logger = new();

    public void Dispose() => _memory.Dispose();

    /// <summary>Redis giả: <c>IDatabase</c> Moq lưu chuỗi đã ghi (kèm expiry) và trả lại khi đọc.</summary>
    private sealed class RedisFake
    {
        public Dictionary<string, (string Value, TimeSpan? Expiry)> Store { get; } = [];

        public Mock<IDatabase> Database { get; } = new();

        public Mock<IConnectionMultiplexer> Multiplexer { get; } = new();

        public RedisFake()
        {
            Database
                .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags flags) =>
                    Store.TryGetValue(key.ToString(), out var entry) ? (RedisValue)entry.Value : RedisValue.Null);
            Database
                .Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .Callback((RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
                    Store[key.ToString()] = (value.ToString(), expiry))
                .ReturnsAsync(true);
            Multiplexer
                .Setup(multiplexer => multiplexer.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(Database.Object);
        }
    }

    private StrategyDecisionCache CreateCache(
        string provider = "Memory",
        bool fallbackToMemory = true,
        IConnectionMultiplexer? redis = null,
        IMemoryCache? memory = null,
        string instanceName = "tadss:")
    {
        var options = MsOptions.Create(new TadssCacheOptions
        {
            Provider = provider,
            EnableFallbackToMemory = fallbackToMemory,
            InstanceName = instanceName,
        });
        return new StrategyDecisionCache(memory ?? _memory, options, _logger, redis);
    }

    private static StrategyDecision CreateDecision(string status) => status switch
    {
        "APPROVED" => StrategyDecision.Approve(0.3m, "OFF_PEAK_DEPOSIT", ["Giờ thấp điểm", "Khách hàng thân thiết"]),
        "NEED_APPROVAL" => StrategyDecision.RequireApproval(
            "HIGH_VALUE_REVIEW",
            ["Giá trị đơn cao"],
            [
                new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Cần Manager duyệt"),
                new DecisionRecommendation(RecommendationType.ALTERNATIVE_SLOT, "Gợi ý khung giờ 18:00"),
            ]),
        "REJECTED" => StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm chính sách đặt sân"]),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static TheoryData<string> AllStatuses => new() { "APPROVED", "NEED_APPROVAL", "REJECTED" };

    private void AssertWarningWithKey() =>
        _logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(Key));

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [MemberData(nameof(AllStatuses))]
    public async Task SetThenGet_MemoryProvider_RoundTripsEachStatus_Theory(string status)
    {
        var cache = CreateCache();
        var decision = CreateDecision(status);

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeSameAs(decision, "tầng memory giữ chính instance bất biến");
        cached.Should().BeEquivalentTo(decision);
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [MemberData(nameof(AllStatuses))]
    public async Task SetThenGet_RedisProvider_RoundTripsViaJson_Theory(string status)
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: false, redis.Multiplexer.Object, instanceName: "khac:");
        var decision = CreateDecision(status);

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().NotBeNull();
        cached.Should().NotBeSameAs(decision, "tầng Redis dựng lại từ JSON");
        cached.Should().BeEquivalentTo(decision);

        // Khóa dùng nguyên văn, không cộng InstanceName; chuỗi ghi là JSON theo TadssJson (camelCase, enum là chuỗi đúng tên)
        redis.Store.Should().ContainKey(Key).And.HaveCount(1);
        using var json = JsonDocument.Parse(redis.Store[Key].Value);
        json.RootElement.GetProperty("status").GetString().Should().Be(status);
        json.RootElement.GetProperty("reasonCode").GetString().Should().Be(decision.ReasonCode);
        json.RootElement.GetProperty("reasons").GetArrayLength().Should().Be(decision.Reasons.Count);
        json.RootElement.GetProperty("recommendations").GetArrayLength().Should().Be(decision.Recommendations.Count);
        json.RootElement.GetProperty("depositRate").ValueKind.Should().Be(
            decision.DepositRate is null ? JsonValueKind.Null : JsonValueKind.Number);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_RedisRecommendationTypes_AreWrittenAsExactEnumNames()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: false, redis.Multiplexer.Object);

        await cache.SetAsync(Key, CreateDecision("NEED_APPROVAL"), TimeSpan.FromMinutes(5), CancellationToken.None);

        using var json = JsonDocument.Parse(redis.Store[Key].Value);
        var types = json.RootElement.GetProperty("recommendations").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .ToArray();
        types.Should().Equal("REQUIRE_APPROVAL", "ALTERNATIVE_SLOT");
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_UsesGivenTtl()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: false, redis.Multiplexer.Object);

        await cache.SetAsync(Key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(60), CancellationToken.None);

        redis.Store[Key].Expiry.Should().Be(TimeSpan.FromMinutes(60), "TTL lấy từ tham số, không phải TtlHours");
        redis.Database.Verify(
            db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), TimeSpan.FromMinutes(60), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_MemoryTier_UsesGivenTtlAndSize()
    {
        var entry = new Mock<ICacheEntry>();
        entry.SetupAllProperties();
        var memory = new Mock<IMemoryCache>();
        memory.Setup(m => m.CreateEntry(It.IsAny<object>())).Returns(entry.Object);
        var cache = CreateCache(memory: memory.Object);

        await cache.SetAsync(Key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(60), CancellationToken.None);

        entry.Object.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(60));
        entry.Object.Size.Should().Be(1, "mỗi mục tính 1 đơn vị để không lỗi khi IMemoryCache có SizeLimit");
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task SetThenGet_MemoryCacheWithSizeLimit_StillCaches()
    {
        using var limited = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = CreateCache(memory: limited);
        var decision = CreateDecision("APPROVED");

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        (await cache.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
        _logger.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_RedisThrows_FallsBackToMemory()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: true, redis.Multiplexer.Object);
        var decision = CreateDecision("NEED_APPROVAL");
        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        redis.Database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis không kết nối được"));

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeSameAs(decision);
        AssertWarningWithKey();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_RedisThrowsAndNoMemoryFallback_ReturnsNull()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: false, redis.Multiplexer.Object);
        redis.Database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("Redis quá hạn", CommandStatus.Unknown));

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeNull();
        AssertWarningWithKey();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_RedisThrowsOperationCanceledWhileCallerNotCancelled_FallsBackToMemory()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: true, redis.Multiplexer.Object);
        var decision = CreateDecision("REJECTED");
        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        redis.Database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new OperationCanceledException("Hủy do tầng Redis, không phải caller"));

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeSameAs(decision);
    }

    public static TheoryData<string, string> CorruptPayloads => new()
    {
        { "json sai cú pháp", "{not json" },
        { "json literal null", "null" },
        { "status lạ", "{\"status\":\"MAYBE\",\"depositRate\":0.3," + ValidTail + "}" },
        { "status chữ thường", "{\"status\":\"approved\",\"depositRate\":0.3," + ValidTail + "}" },
        { "status dạng số", "{\"status\":0,\"depositRate\":0.3," + ValidTail + "}" },
        { "thiếu status", "{\"depositRate\":0.3," + ValidTail + "}" },
        { "rate 1.5", "{\"status\":\"APPROVED\",\"depositRate\":1.5," + ValidTail + "}" },
        { "rate âm", "{\"status\":\"APPROVED\",\"depositRate\":-0.1," + ValidTail + "}" },
        { "APPROVED thiếu rate", "{\"status\":\"APPROVED\",\"depositRate\":null," + ValidTail + "}" },
        { "NEED_APPROVAL kèm rate", "{\"status\":\"NEED_APPROVAL\",\"depositRate\":0.5," + ValidTail + "}" },
        { "REJECTED kèm rate", "{\"status\":\"REJECTED\",\"depositRate\":0.5," + ValidTail + "}" },
        { "reasonCode rỗng", "{\"status\":\"REJECTED\",\"depositRate\":null,\"reasonCode\":\"\",\"reasons\":[\"a\"],\"recommendations\":[]}" },
        { "reasons chứa null", "{\"status\":\"REJECTED\",\"depositRate\":null,\"reasonCode\":\"X\",\"reasons\":[\"a\",null],\"recommendations\":[]}" },
        { "reasons là null", "{\"status\":\"REJECTED\",\"depositRate\":null,\"reasonCode\":\"X\",\"reasons\":null,\"recommendations\":[]}" },
        { "recommendations chứa null", "{\"status\":\"NEED_APPROVAL\",\"depositRate\":null,\"reasonCode\":\"X\",\"reasons\":[\"a\"],\"recommendations\":[null]}" },
        { "recommendation type lạ", "{\"status\":\"NEED_APPROVAL\",\"depositRate\":null,\"reasonCode\":\"X\",\"reasons\":[\"a\"],\"recommendations\":[{\"type\":\"LA\",\"description\":\"d\"}]}" },
        { "recommendation mô tả rỗng", "{\"status\":\"NEED_APPROVAL\",\"depositRate\":null,\"reasonCode\":\"X\",\"reasons\":[\"a\"],\"recommendations\":[{\"type\":\"REQUIRE_APPROVAL\",\"description\":\" \"}]}" },
        { "APPROVED kèm recommendations", "{\"status\":\"APPROVED\",\"depositRate\":0.3,\"reasonCode\":\"X\",\"reasons\":[\"a\"],\"recommendations\":[{\"type\":\"REQUIRE_APPROVAL\",\"description\":\"d\"}]}" },
        { "trường lạ", "{\"status\":\"APPROVED\",\"depositRate\":0.3,\"extra\":1," + ValidTail + "}" },
    };

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [MemberData(nameof(CorruptPayloads))]
    public async Task Get_CorruptPayload_Theory(string scenario, string payload)
    {
        var redis = new RedisFake();
        redis.Store[Key] = (payload, null);
        var cache = CreateCache("Redis", fallbackToMemory: false, redis.Multiplexer.Object);

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeNull(scenario);
        AssertWarningWithKey();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_CorruptRedisPayload_FallsBackToMemory()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: true, redis.Multiplexer.Object);
        var decision = CreateDecision("APPROVED");
        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        redis.Store[Key] = ("{not json", null);

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeSameAs(decision);
        AssertWarningWithKey();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_RedisThrows_DoesNotThrow()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: true, redis.Multiplexer.Object);
        redis.Database
            .Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis đứt kết nối"));
        var decision = CreateDecision("APPROVED");

        var act = async () => await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        await act.Should().NotThrowAsync();
        AssertWarningWithKey();
        _memory.TryGetValue(Key, out StrategyDecision? inMemory).Should().BeTrue("memory vẫn được ghi khi Redis lỗi");
        inMemory.Should().BeSameAs(decision);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_RedisProviderWithoutMemoryFallback_DoesNotWriteMemory()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: false, redis.Multiplexer.Object);

        await cache.SetAsync(Key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(5), CancellationToken.None);

        _memory.TryGetValue(Key, out StrategyDecision? _).Should().BeFalse();
        redis.Store.Should().ContainKey(Key);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_MemoryProvider_NeverTouchesRedis()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Memory", fallbackToMemory: true, redis.Multiplexer.Object);
        var decision = CreateDecision("APPROVED");

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeSameAs(decision);
        redis.Multiplexer.Verify(multiplexer => multiplexer.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task SetThenGet_RedisProviderWithoutMultiplexer_UsesMemory()
    {
        var cache = CreateCache("Redis", fallbackToMemory: false, redis: null);
        var decision = CreateDecision("REJECTED");

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        (await cache.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_MemoryTierThrows_ReturnsNullWithoutThrowing()
    {
        var memory = new Mock<IMemoryCache>();
        memory.Setup(m => m.TryGetValue(It.IsAny<object>(), out It.Ref<object?>.IsAny))
            .Throws(new InvalidOperationException("memory hỏng"));
        var cache = CreateCache(memory: memory.Object);

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeNull();
        AssertWarningWithKey();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_MemoryTierThrows_DoesNotThrow()
    {
        var memory = new Mock<IMemoryCache>();
        memory.Setup(m => m.CreateEntry(It.IsAny<object>())).Throws(new InvalidOperationException("memory hỏng"));
        var cache = CreateCache(memory: memory.Object);

        var act = async () => await cache.SetAsync(Key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(5), CancellationToken.None);

        await act.Should().NotThrowAsync();
        AssertWarningWithKey();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Get_CallerCancelled_Throws()
    {
        var redis = new RedisFake();
        var cache = CreateCache("Redis", fallbackToMemory: true, redis.Multiplexer.Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await cache.GetAsync(Key, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task Set_CallerCancelled_Throws()
    {
        var cache = CreateCache();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await cache.SetAsync(Key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(5), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _memory.TryGetValue(Key, out StrategyDecision? _).Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Memory")]
    [InlineData("Redis")]
    public async Task Get_MissingKey_ReturnsNull_Theory(string provider)
    {
        var redis = new RedisFake();
        var cache = CreateCache(provider, fallbackToMemory: true, redis.Multiplexer.Object);

        var cached = await cache.GetAsync(Key, CancellationToken.None);

        cached.Should().BeNull();
        _logger.Entries.Should().BeEmpty("miss bình thường không phải sự cố");
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetAndSet_BlankKey_Throws_Theory(string key)
    {
        var cache = CreateCache();

        var get = async () => await cache.GetAsync(key, CancellationToken.None);
        var set = async () => await cache.SetAsync(key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(5), CancellationToken.None);

        await get.Should().ThrowAsync<ArgumentException>();
        await set.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Set_NonPositiveTtl_Throws_Theory(int minutes)
    {
        var cache = CreateCache();

        var act = async () => await cache.SetAsync(Key, CreateDecision("APPROVED"), TimeSpan.FromMinutes(minutes), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
