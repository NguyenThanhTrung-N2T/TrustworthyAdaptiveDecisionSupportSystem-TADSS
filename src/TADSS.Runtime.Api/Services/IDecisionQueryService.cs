using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Tra cứu chi tiết quyết định v2 đã lưu (INTEGRATION_SPEC §3.4). Chỉ đọc: không chạy lại strategy, không tính lại gì,
/// không ghi audit.
/// </summary>
public interface IDecisionQueryService
{
    /// <summary>
    /// Dựng chi tiết một quyết định từ bản ghi audit đã lưu, kèm override và outcome nếu có. Tra theo <c>decisionId</c>
    /// nên không lọc theo role (cả <c>PRIMARY</c> lẫn <c>SHADOW</c>).
    /// </summary>
    /// <param name="decisionId">Định danh quyết định do runtime cấp.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Chi tiết quyết định theo hình dạng §3.4.</returns>
    /// <exception cref="TADSS.Runtime.Api.Middlewares.DecisionNotFoundException">Khi không có quyết định với <paramref name="decisionId"/>.</exception>
    /// <exception cref="System.Text.Json.JsonException">Khi payload đã lưu không giải mã được (lỗi dữ liệu, không bị che).</exception>
    /// <exception cref="TADSS.Runtime.Api.Middlewares.PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    /// <exception cref="OperationCanceledException">Khi <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<DecisionDetailDto> GetDetailAsync(Guid decisionId, CancellationToken cancellationToken);
}
