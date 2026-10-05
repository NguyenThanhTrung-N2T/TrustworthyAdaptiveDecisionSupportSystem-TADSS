namespace TADSS.Infrastructure.Options;

/// <summary>
/// Kiểu lỗi mà <c>FaultInjectionStrategyDecorator</c> tiêm vào strategy (A-07). Chỉ có hiệu lực ở môi trường <c>Testing</c>.
/// </summary>
public enum FaultInjectionProfile
{
    /// <summary>Không tiêm lỗi: gọi thẳng strategy thật.</summary>
    None,

    /// <summary>Chờ <c>DelayMs</c> có tôn trọng token hủy rồi gọi strategy thật (FI-02).</summary>
    Delay,

    /// <summary>Ném <see cref="InvalidOperationException"/> với message cố định, không gọi strategy thật (FI-03).</summary>
    Throw,

    /// <summary>Chờ <c>DelayMs</c> mà không nhìn token hủy rồi gọi strategy thật (FI-05).</summary>
    IgnoreToken,

    /// <summary>Quay vòng đồng bộ <c>DelayMs</c> theo <see cref="TimeProvider"/>, không kiểm token, rồi gọi strategy thật (FI-05).</summary>
    BusyCpu
}
