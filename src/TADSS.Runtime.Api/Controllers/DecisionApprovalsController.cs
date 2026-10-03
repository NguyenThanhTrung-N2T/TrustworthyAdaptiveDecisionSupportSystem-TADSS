using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.Application.Common;
using TADSS.Application.Decisions;
using TADSS.Application.Governance;
using TADSS.Runtime.Api.Common;

namespace TADSS.Runtime.Api.Controllers;

/// <summary>
/// API duyệt quyết định (INTEGRATION_SPEC §3.5–3.6): hàng chờ duyệt của chi nhánh và override của Manager. Controller chỉ chuyển tiếp:
/// kiểm tra header (<c>X-Idempotency-Key</c>, <c>X-Actor-Id</c>) và tham số truy vấn, gọi service, trả kết quả. Mọi lỗi đi qua <c>GlobalExceptionHandler</c>.
/// </summary>
[ApiController]
[Route("api/v2/decisions")]
public class DecisionApprovalsController(IDecisionQueryService decisionQueryService, IOverrideService overrideService) : ControllerBase
{
    /// <summary>Media type của mọi phản hồi lỗi (RFC 7807), dùng cho tài liệu OpenAPI.</summary>
    private const string ProblemContentType = "application/problem+json";

    /// <summary>
    /// Lấy một trang hàng chờ duyệt của chi nhánh (<c>GET /api/v2/decisions/pending-approvals</c>).
    /// </summary>
    /// <param name="branchId">Chi nhánh cần lấy hàng chờ (bắt buộc).</param>
    /// <param name="page">Số trang, từ 1 (mặc định 1).</param>
    /// <param name="pageSize">Số mục mỗi trang, 1..100 (mặc định 20).</param>
    /// <param name="cancellationToken">Token hủy của request.</param>
    /// <returns>Trang hàng chờ (INTEGRATION_SPEC §3.5).</returns>
    /// <response code="200">Trang hàng chờ duyệt.</response>
    /// <response code="400">Thiếu <c>branchId</c>, giá trị không đọc được hoặc <c>page</c>/<c>pageSize</c> ngoài miền (<c>VALIDATION_ERROR</c>).</response>
    /// <response code="503">Lưu trữ tạm thời không khả dụng (<c>PERSISTENCE_UNAVAILABLE</c>), kèm <c>Retry-After</c>.</response>
    /// <response code="500">Lỗi nội bộ (<c>INTERNAL_ERROR</c>).</response>
    [HttpGet("pending-approvals")]
    [ProducesResponseType(typeof(PagedResult<PendingApprovalItemDto>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, ProblemContentType)]
    public async Task<ActionResult<PagedResult<PendingApprovalItemDto>>> GetPendingApprovalsAsync(
        [FromQuery] Guid? branchId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (branchId is null)
        {
            throw new ContractValidationException(ErrorCodes.ValidationError, "Thiếu tham số branchId.", "branchId");
        }

        var result = await decisionQueryService.GetPendingApprovalsAsync(
            branchId.Value, page ?? 1, pageSize ?? ContractLimits.DefaultPageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Manager duyệt hoặc từ chối một quyết định đang chờ duyệt (<c>POST /api/v2/decisions/{decisionId}/override</c>). Gửi lại cùng
    /// <c>X-Idempotency-Key</c> và cùng nội dung thì trả lại bản đã lưu.
    /// </summary>
    /// <param name="decisionId">Định danh quyết định do runtime cấp.</param>
    /// <param name="request">Nội dung override (INTEGRATION_SPEC §3.6).</param>
    /// <param name="cancellationToken">Token hủy của request.</param>
    /// <returns>Override đã lưu.</returns>
    /// <response code="200">Override (mới, hoặc bản đã lưu khi gửi lại cùng key và cùng nội dung).</response>
    /// <response code="400">Thiếu/sai header, body sai hình thức hoặc mức cọc không hợp lệ với đơn (<c>MISSING_HEADER</c>, <c>VALIDATION_ERROR</c>).</response>
    /// <response code="404">Không có quyết định, hoặc đó là bản <c>SHADOW</c> (<c>DECISION_NOT_FOUND</c>).</response>
    /// <response code="409">Key đã dùng cho nội dung khác (<c>IDEMPOTENCY_KEY_REUSE</c>), đã có override khác (<c>ALREADY_OVERRIDDEN</c>), quyết định không còn chờ duyệt (<c>NOT_PENDING_APPROVAL</c>) hoặc hết hạn duyệt (<c>APPROVAL_EXPIRED</c>).</response>
    /// <response code="415">Content-Type không phải JSON (mặc định của MVC; không thuộc tập mã lỗi của contract).</response>
    /// <response code="503">Lưu trữ tạm thời không khả dụng (<c>PERSISTENCE_UNAVAILABLE</c>), kèm <c>Retry-After</c>.</response>
    /// <response code="500">Lỗi nội bộ (<c>INTERNAL_ERROR</c>).</response>
    [HttpPost("{decisionId:guid}/override")]
    [ProducesResponseType(typeof(OverrideResponseDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, ProblemContentType)]
    public async Task<ActionResult<OverrideResponseDto>> OverrideAsync(
        Guid decisionId,
        [FromBody] OverrideRequestDto request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = RequestHeaders.RequireIdempotencyKey(Request);
        var actorId = RequestHeaders.RequireActorId(Request);

        var response = await overrideService.OverrideAsync(decisionId, request, actorId, idempotencyKey, cancellationToken);
        return Ok(response);
    }
}
