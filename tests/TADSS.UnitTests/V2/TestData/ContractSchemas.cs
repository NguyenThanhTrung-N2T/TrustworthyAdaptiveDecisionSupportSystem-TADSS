using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace TADSS.UnitTests.V2.TestData;

/// <summary>Nạp JSON schema v2 từ <c>docs/contracts/schemas/v2/</c> (một lần mỗi file) và đánh giá payload.</summary>
internal static class ContractSchemas
{
    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);

    public static EvaluationResults Evaluate(string schemaFile, string json)
    {
        var schema = Cache.GetOrAdd(schemaFile, file =>
            JsonSchema.FromText(File.ReadAllText(RepositoryPaths.Resolve("docs", "contracts", "schemas", "v2", file))));
        var instance = JsonSerializer.Deserialize<JsonElement>(json);

        return schema.Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        });
    }

    /// <summary>Mô tả lỗi để hiển thị khi assertion thất bại.</summary>
    public static string Describe(EvaluationResults results) =>
        string.Join("; ", (results.Details ?? [])
            .Where(detail => !detail.IsValid)
            .SelectMany(detail => (detail.Errors ?? new Dictionary<string, string>())
                .Select(error => $"{detail.InstanceLocation}: {error.Value}")));
}
