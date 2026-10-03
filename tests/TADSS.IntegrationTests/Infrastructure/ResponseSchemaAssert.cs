using FluentAssertions;
using TADSS.UnitTests.TestData;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// CT-01: kiểm tra response 200 của runtime đúng JSON schema v2 (<c>docs/contracts/schemas/v2/</c>), dùng lại
/// <c>ContractSchemas</c> của UnitTests qua <c>Compile Link</c> (C-11) với kiểm <c>format</c> bật. Mọi response 200 trong integration test
/// phải đi qua helper này: <see cref="IsValidDecisionResult"/> cho <c>POST /api/v2/decisions</c>,
/// <see cref="IsValidDecisionDetail"/> cho <c>GET /api/v2/decisions/{decisionId}</c>, <see cref="IsValidOverrideResponse"/> cho <c>/override</c>,
/// <see cref="IsValidOutcomeResponse"/> cho <c>/outcome</c> và <see cref="IsValidPendingApprovals"/> cho <c>GET /api/v2/decisions/pending-approvals</c>
/// (<see cref="DecisionApiTestBase"/> gọi sẵn khi nhận 200). Assertion thất bại kèm đường dẫn và lý do vi phạm.
/// </summary>
public static class ResponseSchemaAssert
{
    /// <summary>Schema response của <c>POST /api/v2/decisions</c>.</summary>
    public const string DecisionResultSchema = "decision-result.schema.json";

    /// <summary>Schema response của <c>GET /api/v2/decisions/{decisionId}</c>.</summary>
    public const string DecisionDetailSchema = "decision-detail.schema.json";

    /// <summary>Khẳng định <paramref name="json"/> hợp lệ theo <c>decision-result.schema.json</c>.</summary>
    public static void IsValidDecisionResult(string json) => IsValid(DecisionResultSchema, json);

    /// <summary>Khẳng định <paramref name="json"/> hợp lệ theo <c>decision-detail.schema.json</c>.</summary>
    public static void IsValidDecisionDetail(string json) => IsValid(DecisionDetailSchema, json);

    /// <summary>Khẳng định <paramref name="json"/> hợp lệ theo <c>override-response.schema.json</c>.</summary>
    public static void IsValidOverrideResponse(string json) => IsValid(ContractSchemas.OverrideResponse, json);

    /// <summary>Khẳng định <paramref name="json"/> hợp lệ theo <c>outcome-response.schema.json</c>.</summary>
    public static void IsValidOutcomeResponse(string json) => IsValid(ContractSchemas.OutcomeResponse, json);

    /// <summary>Khẳng định <paramref name="json"/> hợp lệ theo <c>pending-approvals.schema.json</c>.</summary>
    public static void IsValidPendingApprovals(string json) => IsValid(ContractSchemas.PendingApprovals, json);

    private static void IsValid(string schemaFile, string json)
    {
        var results = ContractSchemas.Evaluate(schemaFile, json);

        results.IsValid.Should().BeTrue("response phải hợp lệ theo {0}: {1}", schemaFile, ContractSchemas.Describe(results));
    }
}
