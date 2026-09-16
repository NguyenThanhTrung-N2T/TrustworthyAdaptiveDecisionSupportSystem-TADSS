namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Cấu trúc phân trang chuẩn tương đồng với SmashCourt-BE.
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu của phần tử trong danh sách.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// Danh sách phần tử của trang hiện tại.
    /// </summary>
    public IEnumerable<T> Items { get; set; } = [];

    /// <summary>
    /// Số thứ tự trang hiện tại (1-based index).
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Kích thước số phần tử tối đa trên mỗi trang.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Tổng số lượng phần tử trên toàn bộ các trang.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Tổng số trang tính toán dựa trên TotalItems và PageSize.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;

    /// <summary>
    /// Cho biết có trang kế tiếp hay không.
    /// </summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>
    /// Cho biết có trang trước đó hay không.
    /// </summary>
    public bool HasPrev => Page > 1;
}
