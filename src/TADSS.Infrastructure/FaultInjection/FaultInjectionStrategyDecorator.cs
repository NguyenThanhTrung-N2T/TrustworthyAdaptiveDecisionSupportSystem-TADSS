using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.FaultInjection;

/// <summary>
/// Decorator tiêm lỗi vào strategy thật theo <see cref="FaultInjectionOptions.Profile"/> (A-07), dùng để chứng minh pipeline chịu lỗi
/// (FI-02, FI-03, FI-05). Giữ nguyên <see cref="StrategyName"/>/<see cref="StrategyVersion"/> của strategy thật (audit và khóa cache không đổi);
/// <see cref="CachePolicy"/> là <see cref="StrategyCachePolicy.Disabled"/> khi có lỗi được tiêm để lỗi luôn tới được pipeline, không bị cache che.
/// <para>
/// Đây là <b>công cụ tiêm lỗi có chủ đích</b> dùng cho chế độ đánh giá (LT-03, gói 5.5) và kiểm thử FI, <b>không phải mã test</b>: nó nằm trong
/// assembly production để bật được bằng cấu hình (<c>Tadss:Testing:FaultInjection</c>) khi chạy trong Docker. Composition root chỉ bọc nó khi môi trường
/// là <c>Testing</c> và <c>Enabled = true</c>; test kiến trúc <c>FaultInjectionRegistrationTests</c> khóa điều này.
/// </para>
/// </summary>
public sealed class FaultInjectionStrategyDecorator : IReservationDecisionStrategy
{
    /// <summary>Message cố định của lỗi do profile <see cref="FaultInjectionProfile.Throw"/> ném ra.</summary>
    public const string ThrowMessage = "Lỗi tiêm có chủ đích (FaultInjection).";

    private readonly IReservationDecisionStrategy _inner;
    private readonly FaultInjectionOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Bọc <paramref name="inner"/> với cấu hình <paramref name="options"/>; thời gian lấy từ <paramref name="timeProvider"/>.</summary>
    public FaultInjectionStrategyDecorator(IReservationDecisionStrategy inner, FaultInjectionOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _inner = inner;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string StrategyName => _inner.StrategyName;

    /// <inheritdoc />
    public string StrategyVersion => _inner.StrategyVersion;

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy =>
        _options.Profile == FaultInjectionProfile.None ? _inner.CachePolicy : StrategyCachePolicy.Disabled;

    /// <inheritdoc />
    public async Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(_options.DelayMs);

        switch (_options.Profile)
        {
            case FaultInjectionProfile.Delay:
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                break;
            case FaultInjectionProfile.Throw:
                throw new InvalidOperationException(ThrowMessage);
            case FaultInjectionProfile.IgnoreToken:
                // Cố ý không truyền token: mô phỏng strategy không hợp tác
                await Task.Delay(delay, _timeProvider, CancellationToken.None).ConfigureAwait(false);
                break;
            case FaultInjectionProfile.BusyCpu:
                SpinSynchronously(delay);
                break;
            case FaultInjectionProfile.None:
                break;
        }

        return await _inner.EvaluateAsync(reservation, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Quay vòng đồng bộ trên luồng gọi (không kiểm token) cho tới khi đủ <paramref name="duration"/> theo <see cref="TimeProvider"/>.</summary>
    private void SpinSynchronously(TimeSpan duration)
    {
        var start = _timeProvider.GetTimestamp();
        while (_timeProvider.GetElapsedTime(start) < duration)
        {
            Thread.SpinWait(100);
        }
    }
}
