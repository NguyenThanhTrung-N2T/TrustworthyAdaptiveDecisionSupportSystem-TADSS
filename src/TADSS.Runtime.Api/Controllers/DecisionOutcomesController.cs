using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using TADSS.Application.Outcomes;
using TADSS.Runtime.Api.Common;

namespace TADSS.Runtime.Api.Controllers;

/// <summary>
/// API ghi kết cục thực tế của quyết định (INTEGRATION_SPEC §3.7). Controller chỉ chuyển tiếp: kiểm tra header <c>X-Idempotency-Key</c>
/// (bắt buộc theo §3.2 nhưng không được lưu), gọi service và trả kết quả. Mọi lỗi đi qua <c>GlobalExceptionHandler</c>.
/// </summary>
[ApiController]
[Route("api/v2/decisions")]
public class DecisionOutcomesController(IOutcomeService outcomeService) : ControllerBase
{
    /// <summary>Media type của mọi phản hồi lỗi (RFC 7807), dùng cho tài liệu OpenAPI.</summary>
    private const string ProblemContentType = "application/problem+json";

    /// <summary>
    /// Ghi outcome của một quyết định (<c>POST /api/v2/decisions/{decisionId}/outcome</c>). Gửi lại cùng nội dung thì trả lại bản đã lưu.
    /// </summary>
    /// <param name="decisionId">Định danh quyết định do runtime cấp.</param>
    /// <param name="request">Kết cục và thời điểm xảy ra (INTEGRATION_SPEC §3.7).</param>
    /// <param name="cancellationToken">Token hủy của request.</param>
    /// <returns>Outcome đã lưu.</returns>
    /// <response code="200">Outcome (mới, hoặc bản đã lưu khi gửi lại cùng nội dung).</response>
    /// <response code="400">Thiếu/sai <c>X-Idempotency-Key</c> hoặc body sai hình thức (<c>MISSING_HEADER</c>, <c>VALIDATION_ERROR</c>).</response>
    /// <response code="404">Không có quyết định, hoặc đó là bản <c>SHADOW</c> (<c>DECISION_NOT_FOUND</c>).</response>
    /// <response code="409">Đã có outcome với nội dung khác (<c>OUTCOME_ALREADY_RECORDED</c>).</response>
    /// <response code="415">Content-Type không phải JSON (mặc định của MVC; không thuộc tập mã lỗi của contract).</response>
    /// <response code="503">Lưu trữ tạm thời không khả dụng (<c>PERSISTENCE_UNAVAILABLE</c>), kèm <c>Retry-After</c>.</response>
    /// <response code="500">Lỗi nội bộ (<c>INTERNAL_ERROR</c>).</response>
    [HttpPost("{decisionId:guid}/outcome")]
    [ProducesResponseType(typeof(OutcomeResponseDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, ProblemContentType)]
    public async Task<ActionResult<OutcomeResponseDto>> RecordAsync(
        Guid decisionId,
        [FromBody] OutcomeRequestDto request,
        CancellationToken cancellationToken)
    {
        // Chỉ kiểm tra theo §3.2: outcome không lưu key (bảng decision_outcomes không có cột này)
        RequestHeaders.RequireIdempotencyKey(Request);

        var response = await outcomeService.RecordAsync(decisionId, request, cancellationToken);
        return Ok(response);
    }
}
