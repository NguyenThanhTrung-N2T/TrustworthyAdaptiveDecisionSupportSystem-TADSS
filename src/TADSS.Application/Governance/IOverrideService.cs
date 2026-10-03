using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.Application.Exceptions;

namespace TADSS.Application.Governance;

/// <summary>
/// Manager duyệt hoặc từ chối một quyết định đang chờ duyệt (INTEGRATION_SPEC §3.6). Một quyết định có tối đa một override (INV-5);
/// gửi lại đúng yêu cầu cũ (cùng <c>X-Idempotency-Key</c> và payload) trả lại bản đã lưu.
/// </summary>
public interface IOverrideService
{
    /// <summary>
    /// Ghi override theo thứ tự kiểm tra: request sai hình thức (400) → không có/SHADOW (404) → đã override (replay, xung đột key, hoặc
    /// <c>ALREADY_OVERRIDDEN</c>) → không còn chờ duyệt hoặc đã có outcome → hết hạn duyệt → ghi. Thua tranh chấp <c>UNIQUE(decision_id)</c> được xử lý như đã có bản thắng.
    /// </summary>
    /// <param name="decisionId">Định danh quyết định.</param>
    /// <param name="request">Nội dung override (<c>APPROVE</c> kèm <c>rate</c> hoặc <c>amount</c>; <c>REJECT</c> không có cọc).</param>
    /// <param name="actorId">Định danh Manager (<c>X-Actor-Id</c>, đã được controller kiểm tra độ dài).</param>
    /// <param name="idempotencyKey">Khóa idempotency (<c>X-Idempotency-Key</c>, đã được controller kiểm tra độ dài).</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Override đã lưu (bản mới hoặc bản cũ khi replay).</returns>
    /// <exception cref="ContractValidationException"><c>VALIDATION_ERROR</c> khi request sai hình thức hoặc số tiền cọc không hợp lệ với đơn.</exception>
    /// <exception cref="DecisionNotFoundException">Khi không có quyết định hoặc đó là bản <c>SHADOW</c>.</exception>
    /// <exception cref="IdempotencyConflictException">Khi cùng key đã được dùng cho override có nội dung khác.</exception>
    /// <exception cref="DecisionStateConflictException"><c>ALREADY_OVERRIDDEN</c>, <c>NOT_PENDING_APPROVAL</c> hoặc <c>APPROVAL_EXPIRED</c>.</exception>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    /// <exception cref="OperationCanceledException">Khi <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<OverrideResponseDto> OverrideAsync(
        Guid decisionId,
        OverrideRequestDto request,
        string actorId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
