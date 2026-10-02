using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Services;

namespace TADSS.Runtime.Api.Controllers;

/// <summary>
/// API quyết định v2 (INTEGRATION_SPEC §3.3–3.4). Controller chỉ chuyển tiếp: kiểm tra header <c>X-Idempotency-Key</c>, gọi service
/// và trả kết quả. Mọi lỗi (header, giải mã body, nghiệp vụ, hạ tầng) đi qua <c>GlobalExceptionHandler</c> nên controller không tự
/// dựng <see cref="ProblemDetails"/>. Các endpoint pending/override/outcome thuộc gói 5.3.
/// </summary>
[ApiController]
[Route("api/v2/decisions")]
public class DecisionsController(IDecisionService decisionService, IDecisionQueryService decisionQueryService) : ControllerBase
{
    /// <summary>Độ dài tối đa của <c>X-Idempotency-Key</c> (INTEGRATION_SPEC §2).</summary>
    public const int MaxIdempotencyKeyLength = 128;

    /// <summary>Media type của mọi phản hồi lỗi (RFC 7807), dùng cho tài liệu OpenAPI.</summary>
    private const string ProblemContentType = "application/problem+json";

    /// <summary>
    /// Đánh giá một yêu cầu đặt sân (<c>POST /api/v2/decisions</c>). Gửi lại cùng <c>X-Idempotency-Key</c> và cùng nội dung thì trả
    /// lại đúng response đã lưu.
    /// </summary>
    /// <param name="request">Request v2 (INTEGRATION_SPEC §3.3).</param>
    /// <param name="cancellationToken">Token hủy của request.</param>
    /// <returns>Quyết định đã đánh giá.</returns>
    /// <response code="200">Quyết định (mới, hoặc bản đã lưu khi replay cùng key và cùng nội dung).</response>
    /// <response code="400">Thiếu/sai <c>X-Idempotency-Key</c>, body sai hình thức (<c>VALIDATION_ERROR</c>) hoặc vi phạm quy tắc domain.</response>
    /// <response code="415">Content-Type không phải JSON (mặc định của MVC; không thuộc tập mã lỗi của contract).</response>
    /// <response code="409">Key đã dùng cho request có nội dung khác (<c>IDEMPOTENCY_KEY_REUSE</c>).</response>
    /// <response code="503">Không ghi được audit (<c>PERSISTENCE_UNAVAILABLE</c>), kèm <c>Retry-After</c>.</response>
    /// <response code="504">Quá thời gian xử lý (<c>GATEWAY_TIMEOUT</c>), kèm <c>Retry-After</c>.</response>
    /// <response code="500">Lỗi nội bộ (<c>INTERNAL_ERROR</c>).</response>
    [HttpPost]
    [ProducesResponseType(typeof(DecisionResultDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, ProblemContentType)]
    public async Task<ActionResult<DecisionResultDto>> EvaluateAsync(
        [FromBody] DecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers[HttpHeaderNames.IdempotencyKey].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ContractValidationException(
                ErrorCodes.MissingHeader,
                $"Thiếu header {HttpHeaderNames.IdempotencyKey}.",
                HttpHeaderNames.IdempotencyKey);
        }

        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw new ContractValidationException(
                ErrorCodes.ValidationError,
                $"Header {HttpHeaderNames.IdempotencyKey} dài tối đa {MaxIdempotencyKeyLength} ký tự.",
                HttpHeaderNames.IdempotencyKey);
        }

        // TraceIdentifier đã được CorrelationIdMiddleware chuẩn hóa (C-07)
        var result = await decisionService.EvaluateAsync(request, idempotencyKey, HttpContext.TraceIdentifier, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Tra cứu chi tiết một quyết định đã lưu (<c>GET /api/v2/decisions/{decisionId}</c>), kèm override và outcome nếu có.
    /// </summary>
    /// <param name="decisionId">Định danh quyết định do runtime cấp.</param>
    /// <param name="cancellationToken">Token hủy của request.</param>
    /// <returns>Chi tiết quyết định (INTEGRATION_SPEC §3.4).</returns>
    /// <response code="200">Chi tiết quyết định.</response>
    /// <response code="404">Không có quyết định với <paramref name="decisionId"/> (<c>DECISION_NOT_FOUND</c>).</response>
    /// <response code="503">Lưu trữ tạm thời không khả dụng (<c>PERSISTENCE_UNAVAILABLE</c>), kèm <c>Retry-After</c>.</response>
    /// <response code="500">Lỗi nội bộ (<c>INTERNAL_ERROR</c>).</response>
    [HttpGet("{decisionId:guid}")]
    [ProducesResponseType(typeof(DecisionDetailDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, ProblemContentType)]
    public async Task<ActionResult<DecisionDetailDto>> GetAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var detail = await decisionQueryService.GetDetailAsync(decisionId, cancellationToken);
        return Ok(detail);
    }
}
