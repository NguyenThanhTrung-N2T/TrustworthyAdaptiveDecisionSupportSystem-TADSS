using System.Text.Json.Nodes;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Contracts;

/// <summary>
/// CT-01 (phần chi tiết quyết định): <c>decision-detail.schema.json</c> chấp nhận ví dụ INTEGRATION_SPEC §3.4 (kể cả khi có
/// override §3.6 và outcome §3.7) và từ chối payload có trường thừa/thiếu, sai kiểu, <c>override</c>/<c>outcome</c> sai hình dạng.
/// </summary>
public class DecisionDetailSchemaTests
{
    private const string DetailSchema = "decision-detail.schema.json";

    /// <summary>
    /// Ví dụ §3.4: quyết định APPROVED do approval window không khả dụng (strategy gốc là NEED_APPROVAL), chưa có
    /// override/outcome. <c>request</c> là ví dụ §3.3, id/thời gian lấy từ các ví dụ cùng spec.
    /// </summary>
    public static string DetailJson => $$"""
        {
          "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
          "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
          "request": {{V2Samples.RequestJson}},
          "result": {
            "status": "APPROVED",
            "strategyStatus": "NEED_APPROVAL",
            "deposit": { "rate": 1, "amount": 228000, "currency": "VND" },
            "reasonCode": "APPROVAL_WINDOW_UNAVAILABLE",
            "reasons": ["Không còn đủ thời gian để Manager duyệt trước giờ bắt đầu, nên áp dụng mức cọc 100% thay cho bước duyệt."],
            "recommendations": [],
            "approvalExpiresAt": null
          },
          "execution": {
            "strategyName": "MockAlternativeDecisionStrategy",
            "strategyVersion": "2.0.0",
            "executionType": "AUTOMATED",
            "executionTimeMs": 3,
            "fallbackUsed": false,
            "failureReason": null,
            "cacheHit": false
          },
          "override": null,
          "outcome": null,
          "createdAt": "2026-11-18T02:15:00.041+00:00",
          "correlationId": "corr-0001"
        }
        """;

    private const string OverrideJson = """
        {
          "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
          "action": "APPROVE",
          "deposit": { "rate": 0.3, "amount": 68400, "currency": "VND" },
          "actorId": "manager-01",
          "reason": "Khách quen, chi nhánh bảo lãnh",
          "overriddenAt": "2026-11-18T03:00:00+00:00"
        }
        """;

    private const string OutcomeJson = """
        {
          "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
          "outcome": "NO_SHOW",
          "occurredAt": "2026-11-20T18:30:00+07:00",
          "reportedAt": "2026-11-20T19:00:02+00:00"
        }
        """;

    public static TheoryData<string> NegativeCases => new()
    {
        "extra-top-level-field",
        "extra-result-field",
        "extra-execution-field",
        "missing-correlation-id",
        "missing-result",
        "missing-strategy-status",
        "missing-override",
        "wrong-type-decision-id",
        "wrong-type-execution-time",
        "wrong-type-cache-hit",
        "unknown-strategy-status",
        "approved-without-deposit",
        "created-at-without-offset",
        "request-missing-field",
        "request-extra-field",
        "override-wrong-shape",
        "override-not-object",
        "override-approve-without-deposit",
        "override-reject-with-deposit",
        "override-extra-field",
        "override-reason-too-short",
        "outcome-wrong-shape",
        "outcome-not-object",
        "outcome-unknown-value",
        "outcome-extra-field",
    };

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = V2Samples.Object(JsonNode.Parse(json));
        mutate(root);
        return root.ToJsonString();
    }

    private static string WithOverrideAndOutcome() => Mutate(DetailJson, root =>
    {
        root["override"] = JsonNode.Parse(OverrideJson);
        root["outcome"] = JsonNode.Parse(OutcomeJson);
    });

    private static string BuildNegative(string caseName)
    {
        switch (caseName)
        {
            case "extra-top-level-field": return Mutate(DetailJson, root => root["extra"] = 1);
            case "extra-result-field": return Mutate(DetailJson, root => V2Samples.Object(root["result"])["extra"] = 1);
            case "extra-execution-field": return Mutate(DetailJson, root => V2Samples.Object(root["execution"])["extra"] = 1);
            case "missing-correlation-id": return Mutate(DetailJson, root => root.Remove("correlationId"));
            case "missing-result": return Mutate(DetailJson, root => root.Remove("result"));
            case "missing-strategy-status": return Mutate(DetailJson, root => V2Samples.Object(root["result"]).Remove("strategyStatus"));
            case "missing-override": return Mutate(DetailJson, root => root.Remove("override"));
            case "wrong-type-decision-id": return Mutate(DetailJson, root => root["decisionId"] = 42);
            case "wrong-type-execution-time": return Mutate(DetailJson, root => V2Samples.Object(root["execution"])["executionTimeMs"] = "3");
            case "wrong-type-cache-hit": return Mutate(DetailJson, root => V2Samples.Object(root["execution"])["cacheHit"] = "false");
            case "unknown-strategy-status": return Mutate(DetailJson, root => V2Samples.Object(root["result"])["strategyStatus"] = "approved");
            case "approved-without-deposit": return Mutate(DetailJson, root => V2Samples.Object(root["result"])["deposit"] = null);
            case "created-at-without-offset": return Mutate(DetailJson, root => root["createdAt"] = "2026-11-18T02:15:00");
            case "request-missing-field": return Mutate(DetailJson, root => V2Samples.Object(root["request"]).Remove("totalPrice"));
            case "request-extra-field": return Mutate(DetailJson, root => V2Samples.Object(root["request"])["extra"] = true);
            case "override-wrong-shape": return Mutate(DetailJson, root => root["override"] = JsonNode.Parse("""{ "action": "APPROVE", "deposit": { "rate": 0.3 } }"""));
            case "override-not-object": return Mutate(DetailJson, root => root["override"] = "APPROVE");
            case "override-approve-without-deposit": return Mutate(WithOverrideAndOutcome(), root => V2Samples.Object(root["override"])["deposit"] = null);
            case "override-reject-with-deposit": return Mutate(WithOverrideAndOutcome(), root => V2Samples.Object(root["override"])["action"] = "REJECT");
            case "override-extra-field": return Mutate(WithOverrideAndOutcome(), root => V2Samples.Object(root["override"])["extra"] = 1);
            case "override-reason-too-short": return Mutate(WithOverrideAndOutcome(), root => V2Samples.Object(root["override"])["reason"] = "ok");
            case "outcome-wrong-shape": return Mutate(DetailJson, root => root["outcome"] = JsonNode.Parse("""{ "outcome": "NO_SHOW" }"""));
            case "outcome-not-object": return Mutate(DetailJson, root => root["outcome"] = "NO_SHOW");
            case "outcome-unknown-value": return Mutate(WithOverrideAndOutcome(), root => V2Samples.Object(root["outcome"])["outcome"] = "LATE");
            case "outcome-extra-field": return Mutate(WithOverrideAndOutcome(), root => V2Samples.Object(root["outcome"])["extra"] = 1);
            default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
        }
    }

    /// <summary>Ví dụ §3.4 (chưa có override/outcome) hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void DetailSchema_IntegrationSpecSample_IsValid()
    {
        var results = ContractSchemas.Evaluate(DetailSchema, DetailJson);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    /// <summary>Có đủ override (§3.6) và outcome (§3.7) vẫn hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void DetailSchema_WithOverrideAndOutcome_IsValid()
    {
        var results = ContractSchemas.Evaluate(DetailSchema, WithOverrideAndOutcome());

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    /// <summary>Override REJECT không có cọc hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void DetailSchema_RejectOverrideWithoutDeposit_IsValid()
    {
        var json = Mutate(WithOverrideAndOutcome(), root =>
        {
            var overrideNode = V2Samples.Object(root["override"]);
            overrideNode["action"] = "REJECT";
            overrideNode["deposit"] = null;
        });

        var results = ContractSchemas.Evaluate(DetailSchema, json);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    /// <summary>Trường thừa/thiếu, sai kiểu, <c>override</c>/<c>outcome</c> sai hình dạng đều bị từ chối.</summary>
    [Theory]
    [Trait("CatalogId", "CT-01")]
    [MemberData(nameof(NegativeCases))]
    public void DetailSchema_NegativeCases_Rejected_Theory(string caseName)
    {
        var json = BuildNegative(caseName);

        ContractSchemas.Evaluate(DetailSchema, json).IsValid.Should().BeFalse($"ca '{caseName}' vi phạm schema");
    }
}
