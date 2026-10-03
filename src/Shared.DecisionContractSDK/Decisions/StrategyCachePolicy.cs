namespace Shared.DecisionContractSDK.Decisions;

/// <summary>
/// Chính sách cache do strategy khai báo: runtime chỉ cache <see cref="StrategyDecision"/> khi strategy cho phép.
/// </summary>
public sealed class StrategyCachePolicy
{
    /// <summary>Không cache.</summary>
    public static StrategyCachePolicy Disabled { get; } = new(isEnabled: false, ttl: null);

    /// <summary>Cho phép cache.</summary>
    public bool IsEnabled { get; }

    /// <summary>Thời gian sống của mục cache; <c>null</c> khi không cache.</summary>
    public TimeSpan? Ttl { get; }

    private StrategyCachePolicy(bool isEnabled, TimeSpan? ttl)
    {
        IsEnabled = isEnabled;
        Ttl = ttl;
    }

    /// <summary>Cho phép cache với thời gian sống <paramref name="ttl"/>.</summary>
    /// <param name="ttl">Thời gian sống, lớn hơn 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Khi <paramref name="ttl"/> không dương.</exception>
    public static StrategyCachePolicy Enabled(TimeSpan ttl)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        return new StrategyCachePolicy(isEnabled: true, ttl);
    }
}
