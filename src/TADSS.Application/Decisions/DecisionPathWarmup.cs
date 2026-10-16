using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Common;
using TADSS.Application.Options;
using TADSS.Application.Routing;
using TADSS.Application.Strategies;

namespace TADSS.Application.Decisions;

/// <summary>
/// Làm nóng đường xử lý quyết định một lần lúc khởi động, trước khi instance nhận request (<see cref="WarmupOptions"/>): mở song song
/// <see cref="WarmupOptions.DatabaseConnections"/> kết nối bằng truy vấn idempotency chỉ đọc (biên dịch luôn truy vấn), đọc cache, rồi
/// <see cref="WarmupOptions.Iterations"/> vòng dựng domain, hash, định tuyến, chạy mọi strategy đã đăng ký qua pipeline và dựng bản ghi audit.
/// <para>
/// KHÔNG ghi audit (bảng append-only), không ghi cache, không gửi shadow, không đếm quyết định; chỉ pipeline ghi vài mẫu thời gian của chính nó.
/// Lỗi của một bước chỉ là cảnh báo (ghi tên kiểu exception) và các bước khác vẫn chạy; hết ngân sách <see cref="WarmupOptions.TimeoutMs"/>
/// thì dừng. Làm nóng không bao giờ làm hỏng khởi động; chỉ hủy của chính caller được lan ra.
/// </para>
/// </summary>
public sealed class DecisionPathWarmup
{
    private const string WarmupKeyPrefix = "warmup-";
    private const string WarmupTimeZone = "Asia/Ho_Chi_Minh";
    private const string WarmupCurrency = "VND";
    private const decimal WarmupPrice = 200_000m;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStrategyRegistry _registry;
    private readonly IStrategyRouter _router;
    private readonly IStrategyPipeline _pipeline;
    private readonly IStrategyDecisionCache _cache;
    private readonly DecisionResponseComposer _composer;
    private readonly BranchCalendar _calendar;
    private readonly WarmupOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionPathWarmup> _logger;

    /// <summary>Khởi tạo bộ làm nóng từ các port và bộ ghép dùng chung với <see cref="DecisionService"/>.</summary>
    public DecisionPathWarmup(
        IServiceScopeFactory scopeFactory,
        IStrategyRegistry registry,
        IStrategyRouter router,
        IStrategyPipeline pipeline,
        IStrategyDecisionCache cache,
        DecisionResponseComposer composer,
        BranchCalendar calendar,
        IOptions<WarmupOptions> options,
        TimeProvider timeProvider,
        ILogger<DecisionPathWarmup> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Chạy một lượt làm nóng. Trả <c>true</c> khi mọi bước xong trong ngân sách; <c>false</c> khi tắt, có bước lỗi hoặc hết ngân sách.
    /// </summary>
    /// <exception cref="OperationCanceledException">Chỉ khi <paramref name="cancellationToken"/> bị hủy.</exception>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
        {
            _logger.LogInformation("{Section}:Enabled = false: bỏ qua làm nóng đường xử lý quyết định.", WarmupOptions.SectionName);
            return false;
        }

        var startedAt = _timeProvider.GetTimestamp();
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.TimeoutMs), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
        try
        {
            var completed = await RunStepAsync("persistence", WarmPersistenceAsync, token).ConfigureAwait(false);
            completed &= await RunStepAsync("cache", WarmCacheAsync, token).ConfigureAwait(false);
            completed &= await RunStepAsync("decision-path", WarmDecisionPathAsync, token).ConfigureAwait(false);

            var elapsedMs = (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            if (completed)
            {
                _logger.LogInformation(
                    "Làm nóng đường xử lý quyết định xong trong {ElapsedMs} ms ({Connections} kết nối, {Iterations} vòng, {StrategyCount} strategy).",
                    elapsedMs,
                    _options.DatabaseConnections,
                    _options.Iterations,
                    _registry.All.Count);
            }
            else
            {
                _logger.LogWarning("Làm nóng đường xử lý quyết định chưa trọn vẹn sau {ElapsedMs} ms; instance vẫn khởi động.", elapsedMs);
            }

            return completed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Làm nóng đường xử lý quyết định vượt ngân sách {TimeoutMs} ms ({Section}:TimeoutMs) và bị dừng; instance vẫn khởi động.",
                _options.TimeoutMs,
                WarmupOptions.SectionName);
            return false;
        }
    }

    private async Task<bool> RunStepAsync(string step, Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            await action(token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chỉ ghi tên kiểu: message của exception có thể chứa chuỗi kết nối hoặc chi tiết nội bộ
            _logger.LogWarning("Làm nóng bước {Step} lỗi ({ExceptionType}); bỏ qua bước này.", step, ex.GetType().Name);
            return false;
        }
    }

    // Mỗi truy vấn chạy trong scope riêng (DbContext riêng) nên các kết nối được mở song song và ở lại trong pool
    private Task WarmPersistenceAsync(CancellationToken token) =>
        Task.WhenAll(Enumerable.Range(0, _options.DatabaseConnections).Select(async _ =>
        {
            var scope = _scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>();
                await store.FindPrimaryByIdempotencyKeyAsync(NewWarmupKey(), token).ConfigureAwait(false);
            }
        }));

    private async Task WarmCacheAsync(CancellationToken token)
    {
        var request = CreateRequest(AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow()));
        var requestHash = RequestHasher.ComputeHash(request);
        foreach (var registered in _registry.All)
        {
            var key = StrategyDecisionCacheKey.Build(
                registered.Strategy.StrategyName, registered.Strategy.StrategyVersion, registered.OptionsFingerprint, requestHash);
            await _cache.GetAsync(key, token).ConfigureAwait(false);
        }
    }

    private async Task WarmDecisionPathAsync(CancellationToken token)
    {
        for (var iteration = 0; iteration < _options.Iterations; iteration++)
        {
            var nowUtc = AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow());
            var request = CreateRequest(nowUtc);
            var reservation = request.ToDomain();
            var requestHash = RequestHasher.ComputeHash(request);
            _ = _router.Route(reservation);
            var context = new DecisionContext(nowUtc, _calendar);

            foreach (var registered in _registry.All)
            {
                var outcome = await _pipeline.ExecuteAsync(registered.Strategy, reservation, context, token).ConfigureAwait(false);

                // Dựng bản ghi + response như request thật (serialize payload, tính cọc) nhưng không lưu
                _ = _composer.Compose(new DecisionComposition(
                    DecisionId: Guid.NewGuid(),
                    Request: request,
                    Reservation: reservation,
                    Strategy: registered,
                    Outcome: outcome,
                    CacheHit: false,
                    NowUtc: nowUtc,
                    RequestHash: requestHash,
                    IdempotencyKey: NewWarmupKey(),
                    CorrelationId: null));
            }
        }
    }

    private static string NewWarmupKey() => WarmupKeyPrefix + Guid.NewGuid().ToString("N");

    // Một đơn đặt sân hợp lệ, ngày mai khung 19:00–21:00 giờ chi nhánh; id ngẫu nhiên nên không trùng request thật nào
    private static DecisionRequestDto CreateRequest(DateTimeOffset nowUtc)
    {
        var start = new DateTimeOffset(nowUtc.UtcDateTime.Date.AddDays(1).AddHours(12), TimeSpan.Zero);
        return new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            BranchTimeZone = WarmupTimeZone,
            Items =
            [
                new ReservationItemDto
                {
                    ResourceId = Guid.NewGuid(),
                    ResourceTypeId = Guid.NewGuid(),
                    StartTime = start,
                    EndTime = start.AddHours(2),
                    Price = WarmupPrice,
                },
            ],
            Customer = new CustomerDto
            {
                CustomerId = Guid.NewGuid(),
                IsGuest = false,
                Tier = CustomerTier.SILVER,
                History = new CustomerHistoryDto { CompletedCount = 3, NoShowCount = 0, CancelledCount = 0 },
            },
            TotalPrice = WarmupPrice,
            Currency = WarmupCurrency,
            Channel = Channel.ONLINE,
            RequestedAt = nowUtc,
        };
    }
}
