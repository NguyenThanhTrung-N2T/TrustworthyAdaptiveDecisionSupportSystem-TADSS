using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Bộ phân giải chiến lược thẩm định quyết định (Strategy Resolver).
/// Quản lý danh mục các chiến lược đã đăng ký và cung cấp cơ chế phân giải O(1).
/// </summary>
public interface IStrategyResolver
{
    /// <summary>
    /// Phân giải chiến lược đang hoạt động dựa trên deployment configuration.
    /// An toàn tuyệt đối tại runtime vì cấu hình đã được kiểm tra (validate) fail-fast lúc startup.
    /// <para>
    /// Đây là phương thức duy nhất được sử dụng trong luồng xử lý API quyết định chính.
    /// </para>
    /// </summary>
    /// <returns>Thể hiện chiến lược <see cref="IReservationDecisionStrategy"/> đang hoạt động.</returns>
    IReservationDecisionStrategy ResolveActive();

    /// <summary>
    /// Phân giải chiến lược theo tên định danh cụ thể.
    /// Ném <see cref="KeyNotFoundException"/> nếu không tìm thấy trong registry.
    /// <para>
    /// Phục vụ cho mục đích diagnostics, unit testing, và kiểm toán hệ thống.
    /// </para>
    /// </summary>
    /// <param name="strategyName">Tên chiến lược cần phân giải.</param>
    /// <returns>Thể hiện chiến lược <see cref="IReservationDecisionStrategy"/> tương ứng.</returns>
    IReservationDecisionStrategy ResolveByName(string strategyName);

    /// <summary>
    /// Liệt kê tất cả các chiến lược đã được nạp vào registry.
    /// </summary>
    /// <returns>Danh sách thông tin định danh các chiến lược đã đăng ký.</returns>
    IReadOnlyList<StrategyRegistration> ListRegistered();
}

/// <summary>
/// Bản ghi thông tin định danh chiến lược đã đăng ký trong runtime registry.
/// </summary>
/// <param name="Name">Tên định danh của chiến lược.</param>
/// <param name="Version">Phiên bản chiến lược.</param>
public record StrategyRegistration(string Name, string Version);
