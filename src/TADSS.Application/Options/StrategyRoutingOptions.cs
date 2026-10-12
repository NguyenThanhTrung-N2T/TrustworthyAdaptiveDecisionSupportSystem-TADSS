namespace TADSS.Application.Options;

/// <summary>
/// Định tuyến strategy theo từng request (<c>Tadss:Routing</c>, A-01): duyệt <see cref="Rules"/> theo thứ tự, <b>rule đầu tiên khớp</b> được chọn;
/// không rule nào khớp thì dùng <see cref="DefaultStrategy"/>. Thay khóa strategy kích hoạt cũ của section <c>Tadss:Strategy</c> (A-03).
/// Nạp lại nóng khi cấu hình đổi (A-02, <c>StrategyRouter</c> theo dõi qua port <c>IRoutingRulesSource</c>); cấu hình sai lúc khởi động thì ứng dụng không khởi động.
/// </summary>
public sealed class StrategyRoutingOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Routing";

    /// <summary>
    /// Strategy dùng khi không rule nào khớp (bắt buộc; khớp <c>IReservationDecisionStrategy.StrategyName</c>, không phân biệt hoa thường).
    /// </summary>
    public string DefaultStrategy { get; set; } = "DefaultDecisionStrategy";

    /// <summary>
    /// Các rule định tuyến theo thứ tự ưu tiên. Rỗng (mặc định) thì mọi request dùng <see cref="DefaultStrategy"/>.
    /// </summary>
    public List<RoutingRuleOptions> Rules { get; set; } = [];
}
