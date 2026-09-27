using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.BenchmarkRunner.Protocol;

namespace TADSS.BenchmarkRunner.Harness;

/// <summary>
/// Quản lý vòng đời Host thực nghiệm độc lập cho từng cặp (Scenario, Concurrency),
/// hỗ trợ đầy đủ 19 kịch bản qua 5 tầng thực nghiệm (Layers A, B, C, D, E).
/// </summary>
public sealed class ScenarioHostController : IAsyncDisposable
{
    private readonly ExperimentScenarioId _scenario;
    private readonly int _concurrency;
    private readonly BenchmarkSuiteConfig _suiteConfig;
    private readonly string _dbName = $"tadss_bench_{Guid.NewGuid():N}";
    private readonly DecisionRequestDto _canonicalLockedPayload;

    private WebApplicationFactory<global::Program>? _factory;
    private ScenarioConfiguration? _scenarioConfig;

    public ScenarioDescriptor Descriptor => ScenarioCatalog.Get(_scenario);
    public int StrategyInvocationCount => _scenarioConfig?.Strategy.TotalInvocations ?? 0;
    public int ObservedRetryCount => _scenarioConfig?.Strategy.RetryInvocations ?? 0;
    public int ObservedInnerTimeoutCount => _scenarioConfig?.Strategy.InnerTimeoutCount ?? 0;

    public bool WasDecisionEvaluatedByStrategy(Guid decisionId) =>
        _scenarioConfig?.Strategy.WasDecisionEvaluatedByStrategy(decisionId) ?? false;

    public ScenarioHostController(
        ExperimentScenarioId scenario,
        int concurrency,
        BenchmarkSuiteConfig suiteConfig)
    {
        _scenario = scenario;
        _concurrency = concurrency;
        _suiteConfig = suiteConfig;
        _canonicalLockedPayload = CreateCanonicalSeedPayload();
    }

    public HttpClient CreateBenchmarkClient(out RunMetadata metadata, int totalRequests, int warmupRequests)
    {
        _scenarioConfig = ScenarioConfigurationFactory.Create(_scenario, _suiteConfig);

        var hostBuilder = new BenchmarkHostBuilder(_scenarioConfig, _dbName);
        _factory = hostBuilder.Build();

        metadata = ScenarioConfigurationFactory.BuildRunMetadata(
            _scenarioConfig,
            _suiteConfig,
            _concurrency,
            totalRequests,
            warmupRequests);

        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_suiteConfig.Execution.HttpClientTimeoutSeconds);
        return client;
    }

    /// <summary>
    /// Thực hiện Pha 1 (Warm-up & Experimental State Lock) trước khi bấm giờ Measurement.
    /// </summary>
    public async Task ExecuteWarmupAndLockStateAsync(HttpClient client, int warmupRequests)
    {
        var descriptor = Descriptor;

        if (descriptor.CircuitMode == CircuitBreakerBenchmarkMode.PreOpenedFastFail)
        {
            // R2: Mở mạch sang OPEN và giữ nguyên OPEN trong suốt pha Measurement
            await TripCircuitBreakerAsync(client);
            for (int i = 0; i < warmupRequests; i++)
            {
                await SendWarmupRequestAsync(
                    client,
                    CreateUniquePayload(i, isWarmup: true),
                    $"WARMUP-{_scenario}-OPEN-{i}-{Guid.NewGuid():N}");
            }
        }
        else if (descriptor.CircuitMode == CircuitBreakerBenchmarkMode.HalfOpenRecoveryTransition)
        {
            // E4: Mở mạch sang OPEN trong pha Warm-up, sau đó chờ hết thời gian ngắt (1s)
            // để pha Measurement bắt đầu đúng lúc mạch chuyển sang HALF-OPEN -> CLOSED!
            await TripCircuitBreakerAsync(client);
            await Task.Delay(_suiteConfig.Resilience.HalfOpenRecoveryWaitBufferMs);
        }
        else if (descriptor.PayloadMode == PayloadDistributionMode.BoundedWorkingSetCycle)
        {
            // C3: Làm nóng JIT và nạp trước 32 khóa đầu tiên vào Bounded LRU Cache
            int capacity = _suiteConfig.FaultTiming.BoundedCacheCapacity;
            for (int i = 0; i < capacity; i++)
            {
                await SendWarmupRequestAsync(
                    client,
                    CreateBoundedCyclePayload(i),
                    $"WARMUP-{_scenario}-{i}-{Guid.NewGuid():N}");
            }
        }
        else if (descriptor.PreWarmCache)
        {
            // G2, C0, E0, E1: Nạp trước _canonicalLockedPayload vào Cache để khóa trạng thái Warm Hit
            for (int i = 0; i < warmupRequests; i++)
            {
                await SendWarmupRequestAsync(
                    client,
                    _canonicalLockedPayload,
                    $"WARMUP-{_scenario}-{i}-{Guid.NewGuid():N}");
            }
        }
        else
        {
            // G0, G1, R0, R1, R3, C1, C2, D0, D1, D2, E2, E3: Làm nóng JIT bằng các khóa riêng biệt
            int effectiveWarmup = _suiteConfig.GetWarmupRequestsFor(_scenario);
            for (int i = 0; i < effectiveWarmup; i++)
            {
                await SendWarmupRequestAsync(
                    client,
                    CreateUniquePayload(i, isWarmup: true),
                    $"WARMUP-{_scenario}-{i}-{Guid.NewGuid():N}");
            }

            // C2 (Cache Thundering Herd): Xóa sạch Cache sau khi làm nóng JIT để đảm bảo tại t=0 của Measurement,
            // tất cả VUs cùng đập vào một khóa lạnh (_canonicalLockedPayload) chưa có trong Cache!
            if (_scenario == ExperimentScenarioId.C2)
            {
                _scenarioConfig?.CustomCacheService.ClearStore();
            }
        }

        // Reset toàn bộ bộ đếm telemetry của Strategy và kích hoạt các bộ tiêm độ trễ cho Pha Measurement
        _scenarioConfig?.Strategy.ResetCounters();
        _scenarioConfig?.ActivateMeasurementPhaseFaults();
    }

    public DecisionRequestDto CreatePayloadForRequest(int requestIndex, bool isWarmup = false)
    {
        if (isWarmup)
        {
            return CreateUniquePayload(requestIndex, isWarmup: true);
        }

        return Descriptor.PayloadMode switch
        {
            PayloadDistributionMode.IdenticalCanonicalKey => _canonicalLockedPayload,

            PayloadDistributionMode.Mixed50PercentHit => (requestIndex % 2 == 0)
                ? _canonicalLockedPayload
                : CreateUniquePayload(requestIndex, isWarmup: false),

            PayloadDistributionMode.Mixed80PercentHit => (requestIndex % _suiteConfig.FaultTiming.CompositeHitModulo != 0)
                ? _canonicalLockedPayload
                : CreateUniquePayload(requestIndex, isWarmup: false),

            PayloadDistributionMode.BoundedWorkingSetCycle =>
                CreateBoundedCyclePayload(requestIndex % _suiteConfig.FaultTiming.BoundedCacheWorkingSetModulo),

            _ => CreateUniquePayload(requestIndex, isWarmup: false)
        };
    }

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task TripCircuitBreakerAsync(HttpClient client)
    {
        int tripRequests = _suiteConfig.Resilience.PreOpenTripRequestCount;
        for (int i = 0; i < tripRequests; i++)
        {
            await SendWarmupRequestAsync(
                client,
                CreateUniquePayload(i, isWarmup: true),
                $"WARMUP-{_scenario}-TRIP-{i}-{Guid.NewGuid():N}");
        }
    }

    private DecisionRequestDto CreateBoundedCyclePayload(int cycleKeyIndex)
    {
        // Thay đổi StartTime theo cycleKeyIndex để RequestHasher (RFC 8785) sinh ra đúng N mã băm SHA-256 tuần hoàn
        var baseStart = BenchmarkDefaults.PayloadSeed.FixedStartTimeUtc.AddDays(cycleKeyIndex + 1);
        return new DecisionRequestDto
        {
            ReservationId = CreateDeterministicGuid(_scenario.ToString(), _concurrency, cycleKeyIndex + 1),
            CustomerId = _canonicalLockedPayload.CustomerId,
            CustomerTier = _canonicalLockedPayload.CustomerTier,
            CourtId = _canonicalLockedPayload.CourtId,
            BranchId = _canonicalLockedPayload.BranchId,
            CourtTypeId = _canonicalLockedPayload.CourtTypeId,
            StartTime = baseStart,
            EndTime = baseStart.AddHours(BenchmarkDefaults.PayloadSeed.DurationHours),
            TotalPrice = _canonicalLockedPayload.TotalPrice,
            DepositAmount = _canonicalLockedPayload.DepositAmount,
            PaymentStatus = _canonicalLockedPayload.PaymentStatus,
            BookingType = _canonicalLockedPayload.BookingType,
            Priority = _canonicalLockedPayload.Priority,
            RequestedAt = baseStart.AddHours(-BenchmarkDefaults.PayloadSeed.RequestedHoursBeforeStart)
        };
    }

    private DecisionRequestDto CreateUniquePayload(int requestIndex, bool isWarmup)
    {
        int warmupOffset = Descriptor.ExperimentLayer == BenchmarkDefaults.Layers.LayerACache
            ? BenchmarkDefaults.PayloadSeed.CacheWarmupIndexOffset
            : BenchmarkDefaults.PayloadSeed.ResilienceWarmupIndexOffset;

        int effectiveIndex = isWarmup ? requestIndex + warmupOffset : requestIndex + 1;
        var deterministicGuid = CreateDeterministicGuid(_scenario.ToString(), _concurrency, effectiveIndex);

        // Tăng nhẹ TotalPrice theo effectiveIndex đối với kịch bản có bật Cache ở chế độ Miss/Mixed
        // để đảm bảo RequestHasher (vốn băm các trường ngữ nghĩa như TotalPrice/Schedule/Customer) luôn sinh ra khóa Cache mới!
        decimal priceVariation = Descriptor.DisableCache ? 0m : effectiveIndex;

        return new DecisionRequestDto
        {
            ReservationId = deterministicGuid,
            CustomerId = _canonicalLockedPayload.CustomerId,
            CustomerTier = _canonicalLockedPayload.CustomerTier,
            CourtId = _canonicalLockedPayload.CourtId,
            BranchId = _canonicalLockedPayload.BranchId,
            CourtTypeId = _canonicalLockedPayload.CourtTypeId,
            StartTime = _canonicalLockedPayload.StartTime,
            EndTime = _canonicalLockedPayload.EndTime,
            TotalPrice = _canonicalLockedPayload.TotalPrice + priceVariation,
            DepositAmount = _canonicalLockedPayload.DepositAmount + priceVariation,
            PaymentStatus = _canonicalLockedPayload.PaymentStatus,
            BookingType = _canonicalLockedPayload.BookingType,
            Priority = _canonicalLockedPayload.Priority,
            RequestedAt = _canonicalLockedPayload.RequestedAt
        };
    }

    private static async Task SendWarmupRequestAsync(HttpClient client, DecisionRequestDto payload, string idempotencyKey)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(payload)
        };
        msg.Headers.Add("X-Idempotency-Key", idempotencyKey);
        await client.SendAsync(msg);
    }

    private static DecisionRequestDto CreateCanonicalSeedPayload()
    {
        var fixedStart = BenchmarkDefaults.PayloadSeed.FixedStartTimeUtc;
        return new DecisionRequestDto
        {
            ReservationId = BenchmarkDefaults.PayloadSeed.CanonicalReservationId,
            CustomerId = BenchmarkDefaults.PayloadSeed.CanonicalCustomerId,
            CustomerTier = BenchmarkDefaults.PayloadSeed.CustomerTier,
            CourtId = BenchmarkDefaults.PayloadSeed.CanonicalCourtId,
            BranchId = BenchmarkDefaults.PayloadSeed.CanonicalBranchId,
            CourtTypeId = null,
            StartTime = fixedStart,
            EndTime = fixedStart.AddHours(BenchmarkDefaults.PayloadSeed.DurationHours),
            TotalPrice = BenchmarkDefaults.PayloadSeed.TotalPrice,
            DepositAmount = BenchmarkDefaults.PayloadSeed.DepositAmount,
            PaymentStatus = BenchmarkDefaults.PayloadSeed.PaymentStatus,
            BookingType = BenchmarkDefaults.PayloadSeed.BookingType,
            Priority = BenchmarkDefaults.PayloadSeed.Priority,
            RequestedAt = fixedStart.AddHours(-BenchmarkDefaults.PayloadSeed.RequestedHoursBeforeStart)
        };
    }

    private static Guid CreateDeterministicGuid(string prefix, int concurrency, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..4], prefix.GetHashCode());
        BitConverter.TryWriteBytes(bytes[4..8], concurrency);
        BitConverter.TryWriteBytes(bytes[8..12], index);
        BitConverter.TryWriteBytes(bytes[12..16], BenchmarkDefaults.PayloadSeed.DeterministicGuidTag);
        return new Guid(bytes);
    }
}
