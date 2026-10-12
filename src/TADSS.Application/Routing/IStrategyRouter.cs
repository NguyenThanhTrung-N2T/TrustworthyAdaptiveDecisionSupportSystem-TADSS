using Shared.DecisionContractSDK.Domain;

namespace TADSS.Application.Routing;

/// <summary>
/// Chọn strategy cho từng request theo bảng định tuyến <c>Tadss:Routing</c> (A-01): rule đầu tiên khớp, không khớp thì strategy mặc định.
/// </summary>
public interface IStrategyRouter
{
    /// <summary>
    /// Định tuyến <paramref name="reservation"/>. Thuần (không I/O), an toàn đa luồng; đọc bảng định tuyến hiện hành (có thể vừa được nạp lại).
    /// </summary>
    /// <param name="reservation">Reservation đã dựng từ request.</param>
    StrategyRoute Route(Reservation reservation);
}
