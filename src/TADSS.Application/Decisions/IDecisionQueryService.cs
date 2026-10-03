using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.Application.Exceptions;

namespace TADSS.Application.Decisions;

/// <summary>
/// Tra cứu quyết định đã lưu: chi tiết (INTEGRATION_SPEC §3.4) và hàng chờ duyệt (§3.5). Chỉ đọc: không chạy lại strategy, không tính lại gì,
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
    /// <exception cref="TADSS.Application.Exceptions.DecisionNotFoundException">Khi không có quyết định với <paramref name="decisionId"/>.</exception>
    /// <exception cref="System.Text.Json.JsonException">Khi payload đã lưu không giải mã được (lỗi dữ liệu, không bị che).</exception>
    /// <exception cref="TADSS.Application.Exceptions.PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    /// <exception cref="OperationCanceledException">Khi <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<DecisionDetailDto> GetDetailAsync(Guid decisionId, CancellationToken cancellationToken);

    /// <summary>
    /// Lấy một trang hàng chờ duyệt của chi nhánh (INTEGRATION_SPEC §3.5): các quyết định <c>PRIMARY</c> <c>NEED_APPROVAL</c> còn hạn duyệt,
    /// chưa có override/outcome và chưa bị bản mới hơn của cùng đơn thay thế, sắp theo hạn duyệt rồi <c>decisionId</c>. Mỗi mục dựng từ cột
    /// của bản ghi và <c>request_payload</c>.
    /// </summary>
    /// <param name="branchId">Chi nhánh cần lấy hàng chờ.</param>
    /// <param name="page">Số trang, bắt đầu từ 1.</param>
    /// <param name="pageSize">Số mục mỗi trang, trong <c>[1, ContractLimits.MaxPageSize]</c>.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Trang kết quả kèm tổng số mục đang chờ của chi nhánh.</returns>
    /// <exception cref="ContractValidationException"><c>VALIDATION_ERROR</c> khi <paramref name="page"/> hoặc <paramref name="pageSize"/> ngoài miền (field <c>page</c>/<c>pageSize</c>).</exception>
    /// <exception cref="System.Text.Json.JsonException">Khi <c>request_payload</c> đã lưu không giải mã được (lỗi dữ liệu, không bị che).</exception>
    /// <exception cref="TADSS.Application.Exceptions.PersistenceUnavailableException">Khi lưu trữ tạm thời không khả dụng.</exception>
    /// <exception cref="OperationCanceledException">Khi <paramref name="cancellationToken"/> bị hủy.</exception>
    Task<PagedResult<PendingApprovalItemDto>> GetPendingApprovalsAsync(
        Guid branchId, int page, int pageSize, CancellationToken cancellationToken);
}
