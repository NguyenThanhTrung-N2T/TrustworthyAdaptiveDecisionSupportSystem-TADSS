namespace TADSS.BenchmarkRunner.Protocol;

/// <summary>
/// Tập trung toàn bộ các hằng số và giá trị mặc định của giao thức thực nghiệm (19 kịch bản across 5 Layers),
/// loại bỏ hoàn toàn magic numbers rải rác trong mã nguồn.
/// </summary>
public static class BenchmarkDefaults
{
    public static readonly IReadOnlyList<ExperimentScenarioId> CoreSevenScenarios =
    [
        ExperimentScenarioId.G0,
        ExperimentScenarioId.G1,
        ExperimentScenarioId.G2,
        ExperimentScenarioId.R0,
        ExperimentScenarioId.R1,
        ExperimentScenarioId.R2,
        ExperimentScenarioId.R3
    ];

    public static readonly IReadOnlyList<ExperimentScenarioId> DefaultScenarios =
    [
        // Layer A: Cache Baseline (3)
        ExperimentScenarioId.G0,
        ExperimentScenarioId.G1,
        ExperimentScenarioId.G2,
        // Layer B: Resilience & Fault Isolation (4)
        ExperimentScenarioId.R0,
        ExperimentScenarioId.R1,
        ExperimentScenarioId.R2,
        ExperimentScenarioId.R3,
        // Layer C: Real-World Cache Pathologies (4)
        ExperimentScenarioId.C0,
        ExperimentScenarioId.C1,
        ExperimentScenarioId.C2,
        ExperimentScenarioId.C3,
        // Layer D: Strategy & Persistence Performance (3)
        ExperimentScenarioId.D0,
        ExperimentScenarioId.D1,
        ExperimentScenarioId.D2,
        // Layer E: Combined Production Stress & Self-Healing (5)
        ExperimentScenarioId.E0,
        ExperimentScenarioId.E1,
        ExperimentScenarioId.E2,
        ExperimentScenarioId.E3,
        ExperimentScenarioId.E4
    ];

    public static readonly IReadOnlyList<int> DefaultConcurrencyLevels = [1, 10, 50, 100];

    public static readonly string DefaultOutputDirectory = Path.Combine("docs", "research", "benchmarks");

    public static class Execution
    {
        /// <summary>
        /// Ngăn chặn độ trễ cấp phát luồng của .NET ThreadPool (1 luồng / 500ms) khi bắn tải đồng thời 50-100 VUs.
        /// </summary>
        public const int MinWorkerThreads = 256;

        public const int HttpClientTimeoutSeconds = 30;
        public const int MaxErrorLogEntriesPerRun = 10;
        public const int DefaultFastScenarioRequests = 400;
        public const int DefaultTimedScenarioRequests = 200;
        public const int DefaultWarmupRequests = 50;
        public const int MaxExceptionDetailLength = 120;
    }

    public static class Resilience
    {
        public const int OuterTimeoutMs = 500;
        public const int InnerTimeoutMs = 120;
        public const int RetryCount = 2;
        public const int RetryDelayMs = 30;
        public const int SamplingDurationSeconds = 60;

        public const int StandardMinimumThroughput = 10;
        public const double StandardFailureRatio = 0.5;
        public const int StandardBreakDurationSeconds = 15;

        /// <summary>
        /// Kịch bản R2: Ngưỡng tối thiểu để mở mạch ngay trong pha Warm-up và giữ mạch OPEN suốt 1 giờ (3600s).
        /// </summary>
        public const int PreOpenedMinimumThroughput = 2;
        public const int PreOpenedBreakDurationSeconds = 3600;
        public const int PreOpenTripRequestCount = 3;

        /// <summary>
        /// Kịch bản E4 (Half-Open Recovery): Mở mạch trong pha Warm-up với thời gian ngắt ngắn (1 giây),
        /// chờ hết thời gian ngắt để đo lường quá trình tự phục hồi OPEN -> HALF-OPEN -> CLOSED trong pha Measurement.
        /// </summary>
        public const int HalfOpenRecoveryBreakDurationSeconds = 1;
        public const int HalfOpenRecoveryWaitBufferMs = 1150;

        /// <summary>
        /// Kịch bản R1 & R3: Khóa ngưỡng mở mạch (int.MaxValue) để đo thuần túy hành vi Retry mà không bị Circuit Breaker cắt ngang.
        /// </summary>
        public const int IsolatedNoBreakMinimumThroughput = int.MaxValue;
        public const double IsolatedNoBreakFailureRatio = 1.0;
    }

    public static class FaultTiming
    {
        /// <summary>
        /// Kịch bản R1: Lần gọi đầu tiên trễ 160ms (> InnerTimeout 120ms) để kích hoạt cắt timeout lần 1, sau đó phục hồi ở lần thử thứ 2.
        /// </summary>
        public const int RecoverableFirstAttemptDelayMs = 160;

        /// <summary>
        /// Kịch bản R2/R3: Mỗi lần gọi trễ 250ms (> InnerTimeout 120ms) để luôn cạn kiệt cả 3 lần thử.
        /// </summary>
        public const int ExhaustiveAttemptDelayMs = 250;

        /// <summary>
        /// Giới hạn số lượng request warm-up cho các kịch bản có tiêm độ trễ để tiết kiệm thời gian khởi động JIT.
        /// </summary>
        public const int TimedScenarioMaxWarmupRequests = 10;

        // Layer C: Cache Pathologies Parameters
        public const int WanCacheMinLatencyMs = 25;
        public const int WanCacheMaxLatencyMs = 45;
        public const int ThunderingHerdColdComputeMinMs = 15;
        public const int ThunderingHerdColdComputeMaxMs = 20;
        public const int BoundedCacheCapacity = 32;
        public const int BoundedCacheWorkingSetModulo = 128;

        // Layer D: Strategy & Persistence Parameters
        public const int HeavyStrategyMinDelayMs = 80;
        public const int HeavyStrategyMaxDelayMs = 105;
        public const int DbContentionMinDelayMs = 20;
        public const int DbContentionMaxDelayMs = 40;
        public const int JitterFastDelayMs = 5;
        public const int JitterSlowDelayMs = 95;
        public const int JitterSlowRequestModulo = 5; // 20% slow (1 in 5)

        // Layer E: Combined Stress Parameters
        public const int CompositeHitModulo = 5; // 80% cache hit (4 in 5), 20% miss
        public const int CompositeFastStrategyMinMs = 5;
        public const int CompositeFastStrategyMaxMs = 10;
        public const int CompositeFastDbMinMs = 2;
        public const int CompositeFastDbMaxMs = 5;

        public const int CompoundWanCacheMinMs = 15;
        public const int CompoundWanCacheMaxMs = 25;
        public const int CompoundStrategyMinMs = 65;
        public const int CompoundStrategyMaxMs = 85;

        public const int TripleFaultWanCacheMinMs = 15;
        public const int TripleFaultWanCacheMaxMs = 25;
        public const int TripleFaultStrategyMinMs = 90;
        public const int TripleFaultStrategyMaxMs = 110;
        public const int TripleFaultDbMinMs = 15;
        public const int TripleFaultDbMaxMs = 25;

        public const int IntermittentFailureModulo = 7; // ~14.3% transient failure on Attempt 1 -> recovered on Attempt 2
    }

    public static class CacheModes
    {
        public const string Memory = "Memory";
        public const string RedisCloud = "RedisCloud";
    }

    public static class Layers
    {
        public const string LayerACache = "LayerA_Cache";
        public const string LayerBResilience = "LayerB_Resilience";
        public const string LayerCCachePathologies = "LayerC_CachePathologies";
        public const string LayerDStrategyPersistence = "LayerD_StrategyAndPersistence";
        public const string LayerECombinedStress = "LayerE_CombinedStress";
    }

    public static class PayloadSeed
    {
        public static readonly DateTime FixedStartTimeUtc = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        public static readonly Guid CanonicalReservationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        public static readonly Guid CanonicalCustomerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        public static readonly Guid CanonicalCourtId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        public static readonly Guid CanonicalBranchId = Guid.Parse("77777777-6666-5555-4444-333333333333");
        public const string CustomerTier = "VIP";
        public const int DurationHours = 2;
        public const decimal TotalPrice = 400000m;
        public const decimal DepositAmount = 400000m;
        public const string PaymentStatus = "PAID";
        public const string BookingType = "SINGLE";
        public const string Priority = "HIGH";
        public const int RequestedHoursBeforeStart = 24;
        public const int CacheWarmupIndexOffset = 100_000;
        public const int ResilienceWarmupIndexOffset = 200_000;
        public const int DeterministicGuidTag = 0x54414453;
    }
}
