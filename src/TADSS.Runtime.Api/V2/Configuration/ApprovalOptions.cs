namespace TADSS.Runtime.Api.V2.Configuration;

/// <summary>
/// Cấu hình chính sách approval window của runtime v2 (INTEGRATION_SPEC §1.1): khoảng thời gian Manager có để
/// duyệt và thời gian tối thiểu phải còn lại trước giờ bắt đầu thì mới cho phép chờ duyệt.
/// </summary>
public class ApprovalOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Approval";

    /// <summary>
    /// Độ dài approval window (phút) tính từ thời điểm đánh giá; phải lớn hơn 0.
    /// </summary>
    public int WindowMinutes { get; set; } = 30;

    /// <summary>
    /// Thời gian tối thiểu (phút) phải còn lại từ lúc hết approval window đến giờ bắt đầu sớm nhất; không âm.
    /// </summary>
    public int MinLeadMinutes { get; set; } = 120;
}
