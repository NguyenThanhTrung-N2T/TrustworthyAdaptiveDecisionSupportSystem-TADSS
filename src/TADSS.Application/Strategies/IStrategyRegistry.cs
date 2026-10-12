using System.Diagnostics.CodeAnalysis;

namespace TADSS.Application.Strategies;

/// <summary>
/// Danh bạ các strategy đã đăng ký, tra theo tên (không phân biệt hoa thường). Việc chọn strategy cho từng request thuộc
/// <c>IStrategyRouter</c> (<c>Tadss:Routing</c>, A-01); registry không còn giữ "strategy đang kích hoạt" (A-03).
/// </summary>
public interface IStrategyRegistry
{
    /// <summary>
    /// Tìm strategy theo tên, không phân biệt hoa thường; tên rỗng hoặc không có thì trả <c>false</c> (không ném).
    /// </summary>
    bool TryResolve(string strategyName, [NotNullWhen(true)] out RegisteredStrategy? strategy);

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
