using System.Collections.ObjectModel;

namespace Shared.DecisionContractSDK.Domain;

public record ReservationMetadata
{
    public IReadOnlyDictionary<string, string> Attributes { get; }

    public ReservationMetadata(IDictionary<string, string>? attributes = null)
    {
        Attributes = attributes != null
            ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(attributes))
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    public string? Get(string key) => Attributes.TryGetValue(key, out var value) ? value : null;
}
