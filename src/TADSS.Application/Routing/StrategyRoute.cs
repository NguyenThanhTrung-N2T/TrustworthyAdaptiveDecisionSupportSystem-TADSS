using TADSS.Application.Strategies;

namespace TADSS.Application.Routing;

/// <summary>
/// Kết quả định tuyến (A-04): strategy được chọn và tên rule đã khớp. <see cref="RuleName"/> chỉ đi vào log tóm tắt quyết định, không lưu DB.
/// </summary>
/// <param name="Strategy">Strategy được chọn (kèm fingerprint tham số).</param>
/// <param name="RuleName">Tên rule đã khớp; <c>null</c> khi dùng strategy mặc định.</param>
public sealed record StrategyRoute(RegisteredStrategy Strategy, string? RuleName);
