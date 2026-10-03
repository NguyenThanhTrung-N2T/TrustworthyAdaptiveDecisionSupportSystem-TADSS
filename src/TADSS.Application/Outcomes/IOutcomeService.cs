using Shared.DecisionContractSDK.DTOs.Outcomes;
using TADSS.Application.Exceptions;

namespace TADSS.Application.Outcomes;

/// <summary>
/// Ghi kết cục thực tế của một quyết định (INTEGRATION_SPEC §3.7). Áp cho mọi quyết định <c>PRIMARY</c> bất kể trạng thái;
/// mỗi quyết định có tối đa một outcome (INV-6).
/// </summary>
public interface IOutcomeService
{
    /// <summary>
    /// Ghi outcome. Đã có outcome cùng <c>outcome</c> và <c>occurredAt</c> (sau khi cắt về µs) thì trả lại bản đã lưu (idempotent theo payload);
    /// khác thì xung đột. <c>X-Idempotency-Key</c> do controller kiểm tra theo §3.2 nhưng không được lưu.
    /// </summary>
    /// <param name="decisionId">Định danh quyết định.</param>
    /// <param name="request">Kết cục và thời điểm xảy ra.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Outcome đã lưu (bản mới hoặc bản cũ khi replay), thời điểm theo UTC.</returns>
    /// <exception cref="DecisionNotFoundException">Khi không có quyết định hoặc đó là bản <c>SHADOW</c>.</exception>
    /// <exception cref="DecisionStateConflictException"><c>OUTCOME_ALREADY_RECORDED</c> khi đã có outcome với nội dung khác.</exception>
    /// <exception cref="PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    /// <exception cref="OperationCanceledException">Khi <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<OutcomeResponseDto> RecordAsync(Guid decisionId, OutcomeRequestDto request, CancellationToken cancellationToken);
}
