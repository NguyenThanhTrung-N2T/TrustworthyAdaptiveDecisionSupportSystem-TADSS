using Polly.CircuitBreaker;
using TADSS.Application.Common;

namespace TADSS.Infrastructure.Resilience;

/// <summary>Ánh xạ <see cref="CircuitState"/> của Polly sang <see cref="CircuitStateValue"/> (giá trị số của gauge), để dashboard không phụ thuộc kiểu của Polly.</summary>
internal static class CircuitStateMapper
{
    /// <summary>Đổi trạng thái breaker của Polly sang giá trị gauge.</summary>
    public static CircuitStateValue ToValue(CircuitState state) => state switch
    {
        CircuitState.Closed => CircuitStateValue.Closed,
        CircuitState.HalfOpen => CircuitStateValue.HalfOpen,
        CircuitState.Open => CircuitStateValue.Open,
        CircuitState.Isolated => CircuitStateValue.Isolated,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Trạng thái breaker không biết."),
    };
}
