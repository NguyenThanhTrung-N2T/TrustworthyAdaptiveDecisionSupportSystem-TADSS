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

    /// <summary>Định danh quyết định đã tra cứu.</summary>
    public Guid DecisionId { get; }
}
