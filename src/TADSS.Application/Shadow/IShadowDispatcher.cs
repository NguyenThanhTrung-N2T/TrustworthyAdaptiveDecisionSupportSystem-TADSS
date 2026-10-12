namespace TADSS.Application.Shadow;

/// <summary>
/// Gửi việc shadow vào hàng đợi sau khi bản <c>PRIMARY</c> đã lưu. Không chờ và không bao giờ ném: shadow không được ảnh hưởng request chính (INV-8).
/// </summary>
public interface IShadowDispatcher
{
    /// <summary>
    /// Gửi <paramref name="item"/> nếu shadow đang bật, strategy chính khác strategy shadow và request được lấy mẫu; hàng đợi đầy thì bỏ việc.
    /// </summary>
    /// <param name="primaryStrategyName">Tên strategy đã xử lý request chính.</param>
    /// <param name="item">Việc shadow.</param>
    void Dispatch(string primaryStrategyName, ShadowWorkItem item);
}
