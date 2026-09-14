using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.Runtime.Api.Services;

namespace TADSS.Runtime.Api.Controllers;

[ApiController]
[Route("api/v1/decisions")]
[Produces("application/json")]
public class DecisionsController : ControllerBase
{
    private readonly IDecisionService _decisionService;
    private readonly ILogger<DecisionsController> _logger;

    public DecisionsController(
        IDecisionService decisionService,
        ILogger<DecisionsController> logger)
    {
        _decisionService = decisionService;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates an incoming reservation decision request.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DecisionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> EvaluateDecision(
        [FromBody] DecisionRequestDto request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Correlation-ID")] string? correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Required Header",
                Detail = "The 'X-Idempotency-Key' request header is required.",
                Instance = HttpContext.Request.Path,
                Type = "https://tadss.smashcourt.vn/errors/missing-header"
            };
            problemDetails.Extensions["code"] = "MISSING_IDEMPOTENCY_KEY";
            return BadRequest(problemDetails);
        }

        // Set correlation ID header
        var effectiveCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString()
            : correlationId;

        Response.Headers["X-Correlation-ID"] = effectiveCorrelationId;

        var result = await _decisionService.EvaluateDecisionAsync(request, idempotencyKey, cancellationToken);

        Response.Headers["X-Decision-ID"] = result.DecisionId.ToString();

        return Ok(result);
    }

    /// <summary>
    /// Retrieves full audit detail for a specific decision.
    /// </summary>
    [HttpGet("{decisionId:guid}")]
    [ProducesResponseType(typeof(DecisionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDecisionDetail(
        [FromRoute] Guid decisionId,
        [FromHeader(Name = "X-Correlation-ID")] string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            Response.Headers["X-Correlation-ID"] = correlationId;
        }

        var detail = await _decisionService.GetDecisionDetailAsync(decisionId, cancellationToken);
        if (detail == null)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Decision Not Found",
                Detail = $"Decision with ID '{decisionId}' was not found.",
                Instance = HttpContext.Request.Path,
                Type = "https://tadss.smashcourt.vn/errors/resource-not-found"
            };
            problemDetails.Extensions["code"] = "DECISION_NOT_FOUND";
            return NotFound(problemDetails);
        }

        return Ok(detail);
    }

    /// <summary>
    /// Retrieves a paginated list of decisions waiting for human manager review.
    /// </summary>
    [HttpGet("pending-approvals")]
    [ProducesResponseType(typeof(PagedResult<PendingApprovalItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPendingApprovals(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromHeader(Name = "X-Correlation-ID")] string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            Response.Headers["X-Correlation-ID"] = correlationId;
        }

        var pendingList = await _decisionService.GetPendingApprovalsAsync(page, pageSize, cancellationToken);
        return Ok(pendingList);
    }

    /// <summary>
    /// Overrides a decision that required manager review.
    /// </summary>
    [HttpPost("{decisionId:guid}/override")]
    [ProducesResponseType(typeof(OverrideDecisionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> OverrideDecision(
        [FromRoute] Guid decisionId,
        [FromBody] OverrideDecisionRequestDto request,
        [FromHeader(Name = "X-Correlation-ID")] string? correlationId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            Response.Headers["X-Correlation-ID"] = correlationId;
        }

        var response = await _decisionService.OverrideDecisionAsync(decisionId, request, cancellationToken);
        if (response == null)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Decision Not Found",
                Detail = $"Decision with ID '{decisionId}' was not found for override.",
                Instance = HttpContext.Request.Path,
                Type = "https://tadss.smashcourt.vn/errors/resource-not-found"
            };
            problemDetails.Extensions["code"] = "DECISION_NOT_FOUND";
            return NotFound(problemDetails);
        }

        return Ok(response);
    }
}
