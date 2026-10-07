namespace TADSS.Application.Common;

/// <summary>
/// Giá trị số của gauge trạng thái circuit breaker (<c>tadss.circuit.state</c>, <c>tadss.cache.circuit.state</c>); dashboard dựa vào các số này.
/// </summary>
public enum CircuitStateValue
{
    /// <summary>Breaker đóng: gọi bình thường.</summary>
    Closed = 0,

    /// <summary>Breaker nửa mở: đang thăm dò.</summary>
    HalfOpen = 1,

    /// <summary>Breaker mở: fast-fail.</summary>
    Open = 2,

    /// <summary>Breaker bị cô lập thủ công.</summary>
    Isolated = 3,
}
