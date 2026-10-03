using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Application.Exceptions;

namespace TADSS.Application.Decisions;

/// <summary>
/// Điều phối vòng đời đánh giá quyết định v2: idempotency, chọn strategy, cache, pipeline chịu lỗi, approval window,
/// quy đổi cọc và ghi audit đồng bộ (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.4, INTEGRATION_SPEC §3.9).
/// </summary>
public interface IDecisionService
{
    /// <summary>
    /// Đánh giá một yêu cầu đặt sân. Cùng <paramref name="idempotencyKey"/> và cùng nội dung nghiệp vụ thì trả lại đúng
    /// response đã lưu mà không chạy lại strategy; khác nội dung thì báo xung đột. Request sai hình thức luôn bị từ chối
    /// (400) kể cả khi key đã từng được dùng, vì domain được dựng từ request trước khi tra idempotency.
    /// </summary>
    /// <param name="request">Request v2 đã deserialize.</param>
    /// <param name="idempotencyKey">Khóa idempotency (1–128 ký tự, không toàn khoảng trắng); controller phải chặn trước.</param>
    /// <param name="correlationId">Mã tương quan của request; <c>null</c> khi không có.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Response v2 của quyết định (mới, hoặc bản đã lưu khi replay).</returns>
    /// <exception cref="ArgumentException">Khi <paramref name="idempotencyKey"/> rỗng, toàn khoảng trắng hoặc dài quá 128 ký tự.</exception>
    /// <exception cref="Shared.DecisionContractSDK.Common.ContractValidationException">Khi request vi phạm ràng buộc hình thức của schema.</exception>
    /// <exception cref="Shared.DecisionContractSDK.Domain.DomainInvariantException">Khi vi phạm quy tắc ngoài schema (thời lượng item, thứ tự giờ).</exception>
    /// <exception cref="TADSS.Application.Exceptions.IdempotencyConflictException">Khi key đã dùng cho request có nội dung khác.</exception>
    /// <exception cref="TADSS.Application.Exceptions.PersistenceUnavailableException">Khi không ghi được audit (fail-closed, INV-10).</exception>
    /// <exception cref="OperationCanceledException">Khi <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<DecisionResultDto> EvaluateAsync(
        DecisionRequestDto request,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken);
}
