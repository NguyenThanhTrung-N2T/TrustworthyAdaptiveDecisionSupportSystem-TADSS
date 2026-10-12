using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TADSS.Application.Common;
using TADSS.Application.Options;
using TADSS.Application.Strategies;

namespace TADSS.Application.Shadow;

/// <summary>
/// Hiện thực <see cref="IShadowDispatcher"/> bằng <see cref="Channel"/> có giới hạn (một reader, <c>FullMode = Wait</c> nhưng chỉ dùng
/// <c>TryWrite</c> nên đầy thì bỏ việc, không chặn). Singleton. Khi <c>Enabled = false</c> không tạo hàng đợi và <see cref="Dispatch"/> không làm gì.
/// Bật mà <c>Strategy</c> không có trong registry thì ném <see cref="InvalidOperationException"/> ngay khi dựng (ứng dụng không khởi động).
/// </summary>
public sealed class ShadowDispatcher : IShadowDispatcher
{
    private static readonly ChannelReader<ShadowWorkItem> EmptyReader = CreateEmptyReader();

    private readonly ShadowOptions _options;
    private readonly TadssMetrics _metrics;
    private readonly ILogger<ShadowDispatcher> _logger;
    private readonly string _shadowStrategyName = "";
    private readonly Channel<ShadowWorkItem>? _channel;

    /// <summary>Khởi tạo dispatcher từ cấu hình <c>Tadss:Shadow</c>.</summary>
    /// <param name="options">Cấu hình shadow.</param>
    /// <param name="registry">Registry strategy (kiểm tên strategy shadow khi bật).</param>
    /// <param name="metrics">Metric của runtime.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="InvalidOperationException">Khi bật shadow mà strategy shadow không có trong registry.</exception>
    public ShadowDispatcher(
        IOptions<ShadowOptions> options,
        IStrategyRegistry registry,
        TadssMetrics metrics,
        ILogger<ShadowDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);
        _options = options.Value;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_options.Enabled)
        {
            return;
        }

        if (!registry.TryResolve(_options.Strategy, out var shadowStrategy))
        {
            var known = string.Join(", ", registry.All.Select(registered => registered.Strategy.StrategyName));
            throw new InvalidOperationException(
                $"{ShadowOptions.SectionName}:{nameof(ShadowOptions.Strategy)} = '{_options.Strategy}' không có trong registry. Strategy hợp lệ: {known}.");
        }

        _shadowStrategyName = shadowStrategy.Strategy.StrategyName;
        _channel = Channel.CreateBounded<ShadowWorkItem>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Đầu đọc của hàng đợi; khi shadow tắt là hàng đợi rỗng đã đóng.</summary>
    public ChannelReader<ShadowWorkItem> Reader => _channel?.Reader ?? EmptyReader;

    /// <inheritdoc />
    public void Dispatch(string primaryStrategyName, ShadowWorkItem item)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            if (string.Equals(primaryStrategyName, _shadowStrategyName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Bỏ qua shadow: strategy chính {StrategyName} trùng strategy shadow.", primaryStrategyName);
                return;
            }

            if (!IsSampled())
            {
                return;
            }

            if (!_channel.Writer.TryWrite(item))
            {
                _metrics.RecordShadowDropped();
                _logger.LogDebug("Hàng đợi shadow đầy: bỏ việc của đơn {ReservationId}.", item.Reservation.ReservationId);
            }
        }
        catch (Exception ex)
        {
            // Shadow không bao giờ được làm hỏng request chính
            _logger.LogWarning("Gửi việc shadow thất bại ({ExceptionType}); bỏ qua.", ex.GetType().Name);
        }
    }

    private bool IsSampled() => _options.SampleRate switch
    {
        >= 1 => true,
        <= 0 => false,
        var rate => Random.Shared.NextDouble() < rate,
    };

    private static ChannelReader<ShadowWorkItem> CreateEmptyReader()
    {
        var channel = Channel.CreateUnbounded<ShadowWorkItem>();
        channel.Writer.Complete();
        return channel.Reader;
    }
}
