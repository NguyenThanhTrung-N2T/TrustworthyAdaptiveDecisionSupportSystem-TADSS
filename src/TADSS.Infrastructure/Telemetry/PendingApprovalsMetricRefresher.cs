using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Telemetry;

/// <summary>
/// Worker nền làm mới gauge <c>tadss.pending_approvals</c> (O-06): mỗi <see cref="TelemetryOptions.PendingApprovalsRefreshSeconds"/> giây đếm số
/// quyết định chờ duyệt theo chi nhánh bằng <see cref="IDecisionQueryStore.CountPendingApprovalsByBranchAsync"/> rồi thay nguyên khối ảnh chụp trong
/// <see cref="TadssMetrics"/>, nên callback xuất metric không bao giờ gọi cơ sở dữ liệu. Chi nhánh từng có bản chờ nhưng nay không còn thì báo
/// <b>0</b> (không để series biến mất). Lỗi truy vấn chỉ log Warning (kiểu exception) và giữ giá trị cũ; worker không dừng. Telemetry tắt thì kết thúc ngay.
/// </summary>
internal sealed class PendingApprovalsMetricRefresher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryOptions _options;
    private readonly TadssMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PendingApprovalsMetricRefresher> _logger;
    private readonly HashSet<Guid> _knownBranches = [];

    /// <summary>Khởi tạo worker.</summary>
    /// <param name="scopeFactory">Tạo scope cho mỗi lần đếm (kho audit là scoped).</param>
    /// <param name="options">Cấu hình telemetry.</param>
    /// <param name="metrics">Metric của runtime.</param>
    /// <param name="timeProvider">Đồng hồ (chu kỳ và mốc <c>nowUtc</c>).</param>
    /// <param name="logger">Logger.</param>
    public PendingApprovalsMetricRefresher(
        IServiceScopeFactory scopeFactory,
        IOptions<TelemetryOptions> options,
        TadssMetrics metrics,
        TimeProvider timeProvider,
        ILogger<PendingApprovalsMetricRefresher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        // Chờ một chu kỳ trước lần đếm đầu để việc khởi động không truy vấn ngay
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PendingApprovalsRefreshSeconds), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ứng dụng đang tắt
        }
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDecisionQueryStore>();
            var nowUtc = AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow());
            var counts = await store.CountPendingApprovalsByBranchAsync(nowUtc, stoppingToken).ConfigureAwait(false);

            var snapshot = new Dictionary<Guid, long>(counts);
            foreach (var branch in _knownBranches.Where(branch => !snapshot.ContainsKey(branch)))
            {
                snapshot[branch] = 0;
            }

            _knownBranches.UnionWith(counts.Keys);
            _metrics.ReplacePendingApprovals(snapshot);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Chỉ ghi tên kiểu: message của exception có thể chứa chi tiết nội bộ (chuỗi kết nối); giữ số cũ
            _logger.LogWarning("Làm mới gauge hàng chờ duyệt thất bại ({ExceptionType}); giữ giá trị cũ.", ex.GetType().Name);
        }
    }
}
