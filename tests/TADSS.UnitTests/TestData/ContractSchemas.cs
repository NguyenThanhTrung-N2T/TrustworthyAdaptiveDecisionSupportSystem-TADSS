using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace TADSS.UnitTests.TestData;

/// <summary>Nạp JSON schema v2 từ <c>docs/contracts/schemas/v2/</c> (một lần mỗi file) và đánh giá payload.</summary>
internal static class ContractSchemas
{
    /// <summary>Schema body của <c>POST /api/v2/decisions/{decisionId}/override</c>.</summary>
    public const string OverrideRequest = "override-request.schema.json";

    /// <summary>Schema response 200 của <c>POST /api/v2/decisions/{decisionId}/override</c>.</summary>
    public const string OverrideResponse = "override-response.schema.json";

    /// <summary>Schema body của <c>POST /api/v2/decisions/{decisionId}/outcome</c>.</summary>
    public const string OutcomeRequest = "outcome-request.schema.json";

    /// <summary>Schema response 200 của <c>POST /api/v2/decisions/{decisionId}/outcome</c>.</summary>
    public const string OutcomeResponse = "outcome-response.schema.json";

    /// <summary>Schema response 200 của <c>GET /api/v2/decisions/pending-approvals</c>.</summary>
    public const string PendingApprovals = "pending-approvals.schema.json";

    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);

    // JsonSchema.Net đăng ký $id vào registry toàn cục ở lần đánh giá đầu; các class test chạy song song cùng gọi
    // lần đầu sẽ ném "Overwriting registered schemas is not permitted", nên đánh giá được tuần tự hóa.
    private static readonly object EvaluationLock = new();

    public static EvaluationResults Evaluate(string schemaFile, string json, bool requireFormatValidation = true)
    {
        var instance = JsonSerializer.Deserialize<JsonElement>(json);

        lock (EvaluationLock)
        {
            var schema = Cache.GetOrAdd(schemaFile, file =>
                JsonSchema.FromText(File.ReadAllText(RepositoryPaths.Resolve("docs", "contracts", "schemas", "v2", file))));

            return schema.Evaluate(instance, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = requireFormatValidation
            });
        }
    }

    /// <summary>Mô tả lỗi để hiển thị khi assertion thất bại.</summary>
    public static string Describe(EvaluationResults results) =>
        string.Join("; ", (results.Details ?? [])
            .Where(detail => !detail.IsValid)
            .SelectMany(detail => (detail.Errors ?? new Dictionary<string, string>())
                .Select(error => $"{detail.InstanceLocation}: {error.Value}")));
}
