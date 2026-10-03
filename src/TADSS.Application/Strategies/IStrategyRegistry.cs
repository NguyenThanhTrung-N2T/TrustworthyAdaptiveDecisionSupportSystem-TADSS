namespace TADSS.Application.Strategies;

/// <summary>
/// Danh bạ các strategy v2: strategy đang kích hoạt (<c>Tadss:Strategy:ActiveStrategyName</c>) và tra theo tên.
/// Việc chọn strategy theo từng request (router) thuộc gói 5.4.
/// </summary>
public interface IStrategyRegistry
{
    /// <summary>
    /// Strategy đang được kích hoạt theo cấu hình.
    /// </summary>
    RegisteredStrategy ResolveActive();

    /// <summary>
    /// Tìm strategy theo tên, không phân biệt hoa thường.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Không có strategy tên đó.</exception>
    RegisteredStrategy ResolveByName(string strategyName);

    /// <summary>
    /// Mọi strategy đã đăng ký, theo thứ tự đăng ký.
    /// </summary>
    IReadOnlyList<RegisteredStrategy> All { get; }
}
