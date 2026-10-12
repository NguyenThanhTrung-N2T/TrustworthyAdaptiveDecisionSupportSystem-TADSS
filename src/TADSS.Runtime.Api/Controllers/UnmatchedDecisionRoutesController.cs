using Microsoft.AspNetCore.Mvc;
using TADSS.Application.Exceptions;

namespace TADSS.Runtime.Api.Controllers;

/// <summary>
/// Đường dẫn dưới <c>api/v2/decisions/</c> mà không endpoint nào khớp (điển hình: <c>decisionId</c> không phải GUID nên ràng buộc <c>:guid</c> của route loại): contract chỉ có
/// một câu trả lời cho "không có quyết định này" là 404 <c>DECISION_NOT_FOUND</c> dạng <c>application/problem+json</c>, không phải 404 rỗng của routing.
/// Route catch-all có độ ưu tiên thấp nhất nên không che các route thật; không xuất hiện trong OpenAPI. Không áp cho <c>/api/v2/decisions</c> (không có đoạn sau) nên
/// sai phương thức ở đó vẫn là 405 của routing.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v2/decisions/{identifier}/{**rest}")]
public class UnmatchedDecisionRoutesController : ControllerBase
{
    /// <summary>Mọi phương thức HTTP: không có quyết định nào ứng với đường dẫn này.</summary>
    /// <param name="identifier">Đoạn đường dẫn ngay sau <c>decisions/</c>.</param>
    /// <param name="rest">Phần đường dẫn còn lại (nếu có).</param>
    /// <returns>Không bao giờ trả về: luôn ném <see cref="DecisionNotFoundException"/> để <c>GlobalExceptionHandler</c> dựng 404 problem+json.</returns>
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    public IActionResult Unmatched(string identifier, string? rest)
    {
        _ = rest;
        throw new DecisionNotFoundException(identifier);
    }
}
