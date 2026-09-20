namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Cấu hình chiến lược thẩm định hoạt động cho TADSS Decision Runtime.
/// Thể hiện chính sách triển khai (Deployment Policy), không phải business logic.
/// </summary>
public class StrategyOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Strategy";

    /// <summary>
    /// Tên chiến lược được kích hoạt mặc định (khớp với IReservationDecisionStrategy.StrategyName).
    /// </summary>
    public string ActiveStrategyName { get; set; } = "DefaultDecisionStrategy";
}
