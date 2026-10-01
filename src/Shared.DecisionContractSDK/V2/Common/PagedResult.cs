using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Kết quả phân trang của contract v2 (INTEGRATION_SPEC §3.5).
/// </summary>
/// <typeparam name="T">Kiểu phần tử.</typeparam>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PagedResult<T>
{
    /// <summary>Các phần tử của trang hiện tại.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Số trang hiện tại, bắt đầu từ 1.</summary>
    public required int Page { get; init; }

    /// <summary>Số phần tử tối đa mỗi trang.</summary>
    public required int PageSize { get; init; }

    /// <summary>Tổng số phần tử của mọi trang.</summary>
    public required int TotalItems { get; init; }

    /// <summary>Tổng số trang.</summary>
    public required int TotalPages { get; init; }

    /// <summary>Còn trang sau.</summary>
    public required bool HasNext { get; init; }

    /// <summary>Có trang trước.</summary>
    public required bool HasPrev { get; init; }

    /// <summary>Tạo một trang và tính các trường suy ra.</summary>
    /// <param name="items">Các phần tử của trang.</param>
    /// <param name="page">Số trang, bắt đầu từ 1.</param>
    /// <param name="pageSize">Số phần tử tối đa mỗi trang (lớn hơn 0).</param>
    /// <param name="totalItems">Tổng số phần tử (không âm).</param>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalItems);

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<T>
        {
            Items = items.ToArray(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            HasNext = page < totalPages,
            HasPrev = page > 1
        };
    }
}
