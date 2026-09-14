namespace Shared.DecisionContractSDK.Domain;

public record ResourceInfo
{
    public Guid CourtId { get; init; }
    public Guid BranchId { get; init; }
    public Guid? CourtTypeId { get; init; }

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
