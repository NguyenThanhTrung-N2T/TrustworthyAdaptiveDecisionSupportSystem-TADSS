namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Value Object đại diện cho tài nguyên vật lý của sân cầu lông (Sân, Chi nhánh, Loại sân).
/// </summary>
public record ResourceInfo
{
    /// <summary>
    /// Định danh sân cụ thể.
    /// </summary>
    public Guid CourtId { get; init; }

    /// <summary>
    /// Định danh chi nhánh sở hữu sân.
    /// </summary>
    public Guid BranchId { get; init; }

    /// <summary>
    /// Định danh loại sân (ví dụ: sân VIP, sân thảm PVC tiêu chuẩn, sân ngoài trời). Có thể null.
    /// </summary>
    public Guid? CourtTypeId { get; init; }

    /// <summary>
    /// Khởi tạo ResourceInfo với kiểm tra tính toàn vẹn của CourtId và BranchId.
    /// </summary>
    /// <param name="courtId">Định danh sân.</param>
    /// <param name="branchId">Định danh chi nhánh.</param>
    /// <param name="courtTypeId">Định danh loại sân (tùy chọn).</param>
    /// <exception cref="DomainInvariantException">Ném ra khi CourtId hoặc BranchId rỗng.</exception>
    public ResourceInfo(Guid courtId, Guid branchId, Guid? courtTypeId = null)
    {
        if (courtId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_COURT_ID", "CourtId must not be empty.", nameof(CourtId));
        }

        if (branchId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_BRANCH_ID", "BranchId must not be empty.", nameof(BranchId));
        }

        CourtId = courtId;
        BranchId = branchId;
        CourtTypeId = courtTypeId;
    }
}
