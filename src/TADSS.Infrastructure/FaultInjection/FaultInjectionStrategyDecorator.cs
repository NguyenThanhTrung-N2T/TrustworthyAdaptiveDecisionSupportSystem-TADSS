using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.FaultInjection;

/// <summary>
/// Decorator tiêm lỗi vào strategy thật theo profile hiện hành của <see cref="FaultInjectionControl"/> (A-07, O-17), dùng để chứng minh pipeline chịu lỗi
/// (FI-02, FI-03, FI-05). Giữ nguyên <see cref="StrategyName"/>/<see cref="StrategyVersion"/> của strategy thật (audit và khóa cache không đổi);
/// <see cref="CachePolicy"/> là <see cref="StrategyCachePolicy.Disabled"/> khi có lỗi được tiêm để lỗi luôn tới được pipeline, không bị cache che.
/// Profile và <c>DelayMs</c> đổi nóng được: mỗi lời gọi đọc <see cref="FaultInjectionControl.Current"/> <b>một lần</b> và dùng bản đó tới hết lời gọi.
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
    private readonly FaultInjectionControl _control;
    private readonly TimeProvider _timeProvider;

    /// <summary>Bọc <paramref name="inner"/> với cấu hình hiện hành của <paramref name="control"/>; thời gian lấy từ <paramref name="timeProvider"/>.</summary>
    public FaultInjectionStrategyDecorator(IReservationDecisionStrategy inner, FaultInjectionControl control, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _inner = inner;
        _control = control;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string StrategyName => _inner.StrategyName;

    /// <inheritdoc />
    public string StrategyVersion => _inner.StrategyVersion;

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy =>
        _control.Current.Profile == FaultInjectionProfile.None ? _inner.CachePolicy : StrategyCachePolicy.Disabled;

    /// <inheritdoc />
    public async Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        // Đọc cấu hình một lần: profile và DelayMs của lời gọi này luôn là một cặp nhất quán dù cấu hình đổi giữa chừng
        var snapshot = _control.Current;
        var delay = TimeSpan.FromMilliseconds(snapshot.DelayMs);

        switch (snapshot.Profile)
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
