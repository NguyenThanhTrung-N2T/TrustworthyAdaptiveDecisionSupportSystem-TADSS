namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Cấu trúc phân trang chuẩn tương đồng với SmashCourt-BE.
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
