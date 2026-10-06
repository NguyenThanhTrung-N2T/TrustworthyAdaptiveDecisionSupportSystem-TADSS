using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TADSS.Application.Options;
using TADSS.Application.Shadow;

namespace TADSS.Infrastructure.Shadow;

/// <summary>
/// Worker nền chạy việc shadow (A-11): đọc tuần tự hàng đợi của <see cref="ShadowDispatcher"/>, mỗi việc một DI scope rồi giao cho
/// <see cref="ShadowExecutor"/>. Một việc lỗi không làm worker dừng. Shadow tắt thì kết thúc ngay. Khi tắt ứng dụng, việc còn trong hàng đợi bị bỏ
/// (log Information số việc bị bỏ).
/// </summary>
public sealed class ShadowExecutionWorker : BackgroundService
{
    private readonly ShadowDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ShadowOptions _options;
    private readonly ILogger<ShadowExecutionWorker> _logger;

    /// <summary>Khởi tạo worker.</summary>
    /// <param name="dispatcher">Hàng đợi việc shadow.</param>
    /// <param name="scopeFactory">Tạo scope cho từng việc (kho audit là scoped).</param>
    /// <param name="options">Cấu hình shadow.</param>
    /// <param name="logger">Logger.</param>
    public ShadowExecutionWorker(
        ShadowDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        IOptions<ShadowOptions> options,
        ILogger<ShadowExecutionWorker> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await foreach (var item in _dispatcher.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOneAsync(item, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ứng dụng đang tắt
        }
        finally
        {
            var dropped = _dispatcher.Reader.CanCount ? _dispatcher.Reader.Count : 0;
            _logger.LogInformation("Worker shadow dừng; bỏ {DroppedCount} việc còn trong hàng đợi.", dropped);
        }
    }

    private async Task RunOneAsync(ShadowWorkItem item, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<ShadowExecutor>();
            await executor.ExecuteAsync(item, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ShadowExecutor đã tự bắt lỗi của việc; đây chỉ là lưới an toàn (vd. dựng scope lỗi) để vòng lặp không dừng
            _logger.LogWarning("Việc shadow gặp lỗi ngoài dự kiến ({ExceptionType}); bỏ qua.", ex.GetType().Name);
        }
    }
}
