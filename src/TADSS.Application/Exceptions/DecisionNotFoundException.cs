namespace TADSS.Application.Exceptions;

/// <summary>
/// Không có quyết định nào với <c>decisionId</c> được tra cứu. Được map thành 404 <c>DECISION_NOT_FOUND</c> và message
/// (tiếng Việt, chỉ chứa định danh do client gửi) được phép trả cho client.
/// </summary>
public sealed class DecisionNotFoundException : Exception
{
    /// <summary>
    /// Khởi tạo lỗi không tìm thấy quyết định.
    /// </summary>
    /// <param name="decisionId">Định danh quyết định đã tra cứu.</param>
    public DecisionNotFoundException(Guid decisionId)
        : base($"Không tìm thấy quyết định '{decisionId}'.")
    {
        DecisionId = decisionId;
    }

    /// <summary>
    /// Khởi tạo lỗi cho định danh không thể là <c>decisionId</c> (không phải GUID): không có quyết định nào mang định danh đó nên cũng là 404, đúng contract.
    /// </summary>
    /// <param name="rawIdentifier">Giá trị client gửi trên đường dẫn; message chỉ giữ tối đa <see cref="MaxEchoedLength"/> ký tự.</param>
    public DecisionNotFoundException(string rawIdentifier)
        : base($"Không tìm thấy quyết định '{Truncate(rawIdentifier)}'.")
    {
        DecisionId = Guid.Empty;
    }

    /// <summary>Số ký tự tối đa của định danh sai hình thức được lặp lại trong message.</summary>
    public const int MaxEchoedLength = 64;

    /// <summary>Định danh quyết định đã tra cứu (<see cref="Guid.Empty"/> khi client gửi giá trị không phải GUID).</summary>
    public Guid DecisionId { get; }

    private static string Truncate(string value) => value.Length <= MaxEchoedLength ? value : value[..MaxEchoedLength];
}
