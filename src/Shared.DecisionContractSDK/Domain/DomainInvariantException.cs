namespace Shared.DecisionContractSDK.Domain;

public class DomainInvariantException : Exception
{
    public string InvariantCode { get; }
    public string? FieldName { get; }

    public DomainInvariantException(string invariantCode, string message, string? fieldName = null)
        : base(message)
    {
        InvariantCode = invariantCode;
        FieldName = fieldName;
    }
}
