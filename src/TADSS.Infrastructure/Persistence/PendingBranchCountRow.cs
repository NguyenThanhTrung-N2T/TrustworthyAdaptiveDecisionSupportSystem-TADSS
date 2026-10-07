namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Một dòng kết quả của câu đếm hàng chờ gộp theo chi nhánh (<see cref="PendingApprovalsQuery.CountByBranchAsync"/>); tên thuộc tính
/// khớp bí danh cột (<c>"BranchId"</c>, <c>"Count"</c>) để <c>SqlQuery</c> nạp được kiểu không ánh xạ.
/// </summary>
internal sealed class PendingBranchCountRow
{
    /// <summary>Chi nhánh.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Số quyết định đang chờ duyệt.</summary>
    public long Count { get; set; }
}
