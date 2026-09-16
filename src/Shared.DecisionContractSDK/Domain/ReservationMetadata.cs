using System.Collections.ObjectModel;

namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Value Object chứa metadata bổ sung dưới dạng key-value bất biến phục vụ mở rộng ngữ cảnh đánh giá.
/// </summary>
public record ReservationMetadata
{
    /// <summary>
    /// Danh sách thuộc tính mở rộng (Read-only).
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>
    /// Khởi tạo ReservationMetadata từ dictionary.
    /// </summary>
    /// <param name="attributes">Dictionary thuộc tính ban đầu (tùy chọn).</param>
    public ReservationMetadata(IDictionary<string, string>? attributes = null)
    {
        Attributes = attributes != null
            ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(attributes))
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    /// <summary>
    /// Lấy giá trị của một key metadata cụ thể.
    /// </summary>
    /// <param name="key">Khóa cần tra cứu.</param>
    /// <returns>Giá trị tương ứng nếu tìm thấy; ngược lại trả về null.</returns>
    public string? Get(string key) => Attributes.TryGetValue(key, out var value) ? value : null;
}
