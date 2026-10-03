using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Outcomes;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Sdk.Contracts;

/// <summary>
/// CT-01 (phần quản trị): năm schema viết tay <c>override-request</c>, <c>override-response</c>, <c>outcome-request</c>, <c>outcome-response</c>,
/// <c>pending-approvals</c> chấp nhận ví dụ INTEGRATION_SPEC §3.5–3.7 (và JSON do SDK sinh ra) và từ chối payload sai hình dạng.
/// Độ dài <c>reason</c> đếm theo code point (F-17): emoji ngoài BMP tính là một ký tự.
/// </summary>
public class GovernanceSchemaTests
{
    private const string OverrideApprove = """{"action":"APPROVE","deposit":{"rate":0.3},"reason":"Khách quen, chi nhánh bảo lãnh"}""";

    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");

    private static JsonObject ApproveWithRate() => Parse(OverrideApprove);

    private static JsonObject Parse(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("JSON mẫu không phải object.");

    private static JsonObject Mutate(string json, Action<JsonObject> mutate)
    {
        var root = Parse(json);
        mutate(root);
        return root;
    }

    private static bool IsValid(string schema, JsonNode node) => ContractSchemas.Evaluate(schema, node.ToJsonString()).IsValid;

    private static string Emoji(int count) => string.Concat(Enumerable.Repeat("\U0001F600", count));

    /// <summary>Các ca âm của schema override-request (F-04, F-17 ở mức schema); mỗi ca phải bị từ chối.</summary>
    public static TheoryData<string> OverrideRequestNegativeCases => new()
    {
        "extra-root-field",
        "action-lowercase",
        "action-numeric",
        "action-missing",
        "reason-missing",
        "reason-too-short",
        "reason-four-emoji",
        "reason-over-500",
        "reason-501-emoji",
        "reason-whitespace-only",
        "reason-not-string",
        "approve-without-deposit",
        "approve-with-null-deposit",
        "approve-with-empty-deposit",
        "approve-with-rate-and-amount",
        "reject-with-deposit",
        "rate-negative",
        "rate-above-one",
        "rate-five-decimals",
        "rate-as-string",
        "amount-negative",
        "amount-as-string",
        "deposit-extra-field",
        "deposit-currency-field",
    };

    private static JsonObject OverrideRequestCase(string name) => name switch
    {
        "extra-root-field" => Mutate(OverrideApprove, root => root["unexpected"] = 1),
        "action-lowercase" => Mutate(OverrideApprove, root => root["action"] = "approve"),
        "action-numeric" => Mutate(OverrideApprove, root => root["action"] = 0),
        "action-missing" => Mutate(OverrideApprove, root => root.Remove("action")),
        "reason-missing" => Mutate(OverrideApprove, root => root.Remove("reason")),
        "reason-too-short" => Mutate(OverrideApprove, root => root["reason"] = "abcd"),
        "reason-four-emoji" => Mutate(OverrideApprove, root => root["reason"] = Emoji(4)),
        "reason-over-500" => Mutate(OverrideApprove, root => root["reason"] = new string('x', 501)),
        "reason-501-emoji" => Mutate(OverrideApprove, root => root["reason"] = Emoji(501)),
        "reason-whitespace-only" => Mutate(OverrideApprove, root => root["reason"] = "       "),
        "reason-not-string" => Mutate(OverrideApprove, root => root["reason"] = 12345),
        "approve-without-deposit" => Mutate(OverrideApprove, root => root.Remove("deposit")),
        "approve-with-null-deposit" => Mutate(OverrideApprove, root => root["deposit"] = null),
        "approve-with-empty-deposit" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject()),
        "approve-with-rate-and-amount" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 0.3, ["amount"] = 68400 }),
        "reject-with-deposit" => Mutate(OverrideApprove, root => root["action"] = "REJECT"),
        "rate-negative" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = -0.1 }),
        "rate-above-one" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 1.0001 }),
        "rate-five-decimals" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 0.12345 }),
        "rate-as-string" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = "0.3" }),
        "amount-negative" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["amount"] = -1 }),
        "amount-as-string" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["amount"] = "68400" }),
        "deposit-extra-field" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 0.3, ["currency"] = "VND" }),
        "deposit-currency-field" => Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["amount"] = 68400, ["currency"] = "VND" }),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Ca âm không tồn tại."),
    };

    /// <summary>Mọi ca âm của override-request bị schema từ chối.</summary>
    [Theory]
    [Trait("CatalogId", "CT-01")]
    [MemberData(nameof(OverrideRequestNegativeCases))]
    public void OverrideRequest_SchemaNegativeCases_Theory(string caseName)
    {
        var node = OverrideRequestCase(caseName);

        ContractSchemas.Evaluate(ContractSchemas.OverrideRequest, node.ToJsonString()).IsValid
            .Should().BeFalse("ca '{0}' phải bị schema từ chối", caseName);
    }

    /// <summary>Các payload hợp lệ của override-request: APPROVE theo rate/amount (kể cả biên), REJECT không deposit, độ dài reason theo code point.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void OverrideRequest_ValidPayloads_AreAccepted()
    {
        var valid = new Dictionary<string, JsonObject>
        {
            ["approve-rate"] = ApproveWithRate(),
            ["approve-rate-zero"] = Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 0 }),
            ["approve-rate-one"] = Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 1 }),
            ["approve-rate-four-decimals"] = Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["rate"] = 0.1235 }),
            ["approve-amount"] = Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["amount"] = 68400 }),
            ["approve-amount-zero"] = Mutate(OverrideApprove, root => root["deposit"] = new JsonObject { ["amount"] = 0 }),
            ["reject"] = Mutate(OverrideApprove, root =>
            {
                root["action"] = "REJECT";
                root.Remove("deposit");
            }),
            ["reason-five-chars"] = Mutate(OverrideApprove, root => root["reason"] = "abcde"),
            ["reason-five-emoji"] = Mutate(OverrideApprove, root => root["reason"] = Emoji(5)),
            ["reason-500-emoji"] = Mutate(OverrideApprove, root => root["reason"] = Emoji(500)),
            ["reason-500-chars"] = Mutate(OverrideApprove, root => root["reason"] = new string('x', 500)),
        };

        foreach (var (name, node) in valid)
        {
            var results = ContractSchemas.Evaluate(ContractSchemas.OverrideRequest, node.ToJsonString());
            results.IsValid.Should().BeTrue("ca '{0}': {1}", name, ContractSchemas.Describe(results));
        }
    }

    /// <summary>JSON do SDK sinh ra cho <see cref="OverrideResponseDto"/> (duyệt và từ chối) hợp lệ theo override-response.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void OverrideResponse_SdkSerialized_IsValid()
    {
        var approve = FakeOverrideService.SampleResponse(DecisionId);
        var reject = new OverrideResponseDto
        {
            DecisionId = DecisionId,
            Action = Shared.DecisionContractSDK.Enums.OverrideAction.REJECT,
            Deposit = null,
            ActorId = "manager-01",
            Reason = "Không đủ sân",
            OverriddenAt = approve.OverriddenAt,
        };

        foreach (var response in new[] { approve, reject })
        {
            var json = JsonSerializer.Serialize(response, TadssJson.Options);
            var results = ContractSchemas.Evaluate(ContractSchemas.OverrideResponse, json);
            results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
        }
    }

    /// <summary>Ca âm của override-response: sai INV-7 (APPROVE không cọc, REJECT có cọc), trường thừa/thiếu, reason ngắn.</summary>
    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData("approve-null-deposit")]
    [InlineData("reject-with-deposit")]
    [InlineData("deposit-missing-currency")]
    [InlineData("extra-field")]
    [InlineData("missing-actor")]
    [InlineData("reason-short")]
    [InlineData("overridden-at-without-offset")]
    [InlineData("rate-above-one")]
    public void OverrideResponse_InvalidPayload_IsRejected(string caseName)
    {
        var node = JsonSerializer.SerializeToNode(FakeOverrideService.SampleResponse(DecisionId), TadssJson.Options) as JsonObject
            ?? throw new InvalidOperationException("Không phải object.");
        switch (caseName)
        {
            case "approve-null-deposit":
                node["deposit"] = null;
                break;
            case "reject-with-deposit":
                node["action"] = "REJECT";
                break;
            case "deposit-missing-currency":
                ContractSamples.Object(node["deposit"]).Remove("currency");
                break;
            case "extra-field":
                node["unexpected"] = 1;
                break;
            case "missing-actor":
                node.Remove("actorId");
                break;
            case "reason-short":
                node["reason"] = "abc";
                break;
            case "overridden-at-without-offset":
                node["overriddenAt"] = "2026-11-18T03:00:00";
                break;
            case "rate-above-one":
                ContractSamples.Object(node["deposit"])["rate"] = 1.5;
                break;
        }

        IsValid(ContractSchemas.OverrideResponse, node).Should().BeFalse(caseName);
    }

    /// <summary>Ví dụ §3.7 và JSON do SDK sinh ra hợp lệ theo outcome-request/outcome-response; mọi giá trị enum hợp lệ được chấp nhận.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void OutcomeSchemas_ValidPayloads_AreAccepted()
    {
        const string request = """{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00"}""";
        var response = JsonSerializer.Serialize(FakeOutcomeService.SampleResponse(DecisionId), TadssJson.Options);
        var sdkRequest = JsonSerializer.Serialize(
            new OutcomeRequestDto { Outcome = Shared.DecisionContractSDK.Enums.DecisionOutcome.COMPLETED, OccurredAt = DateTimeOffset.UnixEpoch },
            TadssJson.Options);

        ContractSchemas.Evaluate(ContractSchemas.OutcomeRequest, request).IsValid.Should().BeTrue();
        ContractSchemas.Evaluate(ContractSchemas.OutcomeRequest, sdkRequest).IsValid.Should().BeTrue();
        ContractSchemas.Evaluate(ContractSchemas.OutcomeResponse, response).IsValid.Should().BeTrue();
        foreach (var name in Enum.GetNames<Shared.DecisionContractSDK.Enums.DecisionOutcome>())
        {
            var json = $$"""{"outcome":"{{name}}","occurredAt":"2026-11-20T18:30:00Z"}""";
            ContractSchemas.Evaluate(ContractSchemas.OutcomeRequest, json).IsValid.Should().BeTrue(name);
        }
    }

    /// <summary>Ca âm của outcome-request/outcome-response bị schema từ chối.</summary>
    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":"NO_SHOW"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":"no_show","occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":"UNKNOWN","occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":1,"occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+0700"}""")]
    [InlineData(ContractSchemas.OutcomeRequest, """{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00","reportedAt":"2026-11-20T19:00:00Z"}""")]
    [InlineData(ContractSchemas.OutcomeResponse, """{"decisionId":"0f9c7e5a-0000-4000-8000-00000000d001","outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData(ContractSchemas.OutcomeResponse, """{"decisionId":"not-a-uuid","outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00","reportedAt":"2026-11-20T19:00:02+00:00"}""")]
    [InlineData(ContractSchemas.OutcomeResponse, """{"decisionId":"0f9c7e5a-0000-4000-8000-00000000d001","outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00","reportedAt":"2026-11-20T19:00:02"}""")]
    public void OutcomeSchemas_InvalidPayload_IsRejected(string schema, string json)
    {
        ContractSchemas.Evaluate(schema, json).IsValid.Should().BeFalse(json);
    }

    private static string PendingJson(Action<JsonObject>? mutate = null)
    {
        var root = Parse("""
            {
              "items": [
                {
                  "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
                  "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
                  "branchId": "1f2e3d4c-0000-4000-8000-000000000001",
                  "totalPrice": 2400000,
                  "currency": "VND",
                  "earliestStart": "2026-11-20T18:00:00+07:00",
                  "customer": { "isGuest": false, "tier": "GOLD" },
                  "reasonCode": "HIGH_VALUE_NEED_APPROVAL",
                  "fallbackUsed": false,
                  "approvalExpiresAt": "2026-11-18T02:45:00+00:00",
                  "createdAt": "2026-11-18T02:15:00.041+00:00"
                }
              ],
              "page": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1, "hasNext": false, "hasPrev": false
            }
            """);
        mutate?.Invoke(root);
        return root.ToJsonString();
    }

    private static JsonObject FirstItem(JsonObject root) =>
        root["items"]?[0] as JsonObject ?? throw new InvalidOperationException("Không có item.");

    /// <summary>Ví dụ hàng chờ (§3.5) hợp lệ, kể cả trang rỗng.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void PendingApprovalsSchema_Samples_AreValid()
    {
        var empty = PendingJson(root =>
        {
            root["items"] = new JsonArray();
            root["totalItems"] = 0;
            root["totalPages"] = 0;
        });

        var sample = ContractSchemas.Evaluate(ContractSchemas.PendingApprovals, PendingJson());
        sample.IsValid.Should().BeTrue(ContractSchemas.Describe(sample));
        ContractSchemas.Evaluate(ContractSchemas.PendingApprovals, empty).IsValid.Should().BeTrue();
    }

    /// <summary>Ca âm của hàng chờ: trường thừa/thiếu, phân trang ngoài miền, khách/ hạng sai.</summary>
    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData("extra-root")]
    [InlineData("missing-total-pages")]
    [InlineData("page-zero")]
    [InlineData("page-size-zero")]
    [InlineData("page-size-101")]
    [InlineData("total-items-negative")]
    [InlineData("items-null")]
    [InlineData("item-extra-field")]
    [InlineData("item-missing-expiry")]
    [InlineData("item-null-expiry")]
    [InlineData("item-expiry-without-offset")]
    [InlineData("item-bad-currency")]
    [InlineData("item-bad-tier")]
    [InlineData("item-customer-extra")]
    [InlineData("item-customer-missing-tier")]
    [InlineData("item-negative-price")]
    public void PendingApprovalsSchema_InvalidPayload_IsRejected(string caseName)
    {
        var json = PendingJson(root =>
        {
            switch (caseName)
            {
                case "extra-root":
                    root["unexpected"] = 1;
                    break;
                case "missing-total-pages":
                    root.Remove("totalPages");
                    break;
                case "page-zero":
                    root["page"] = 0;
                    break;
                case "page-size-zero":
                    root["pageSize"] = 0;
                    break;
                case "page-size-101":
                    root["pageSize"] = 101;
                    break;
                case "total-items-negative":
                    root["totalItems"] = -1;
                    break;
                case "items-null":
                    root["items"] = null;
                    break;
                case "item-extra-field":
                    FirstItem(root)["unexpected"] = 1;
                    break;
                case "item-missing-expiry":
                    FirstItem(root).Remove("approvalExpiresAt");
                    break;
                case "item-null-expiry":
                    FirstItem(root)["approvalExpiresAt"] = null;
                    break;
                case "item-expiry-without-offset":
                    FirstItem(root)["approvalExpiresAt"] = "2026-11-18T02:45:00";
                    break;
                case "item-bad-currency":
                    FirstItem(root)["currency"] = "vnd";
                    break;
                case "item-bad-tier":
                    ContractSamples.Object(FirstItem(root)["customer"])["tier"] = "BRONZE";
                    break;
                case "item-customer-extra":
                    ContractSamples.Object(FirstItem(root)["customer"])["customerId"] = "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11";
                    break;
                case "item-customer-missing-tier":
                    ContractSamples.Object(FirstItem(root)["customer"]).Remove("tier");
                    break;
                case "item-negative-price":
                    FirstItem(root)["totalPrice"] = -1;
                    break;
            }
        });

        ContractSchemas.Evaluate(ContractSchemas.PendingApprovals, json).IsValid.Should().BeFalse(caseName);
    }

    /// <summary>JSON do SDK sinh ra cho <see cref="PagedResult{T}"/> của <see cref="PendingApprovalItemDto"/> hợp lệ theo pending-approvals.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void PendingApprovalsSchema_SdkSerializedPage_IsValid()
    {
        var item = new PendingApprovalItemDto
        {
            DecisionId = DecisionId,
            ReservationId = Guid.NewGuid(),
            BranchId = GovernanceTestData.BranchId,
            TotalPrice = 2_400_000m,
            Currency = "VND",
            EarliestStart = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)),
            Customer = new PendingApprovalCustomerDto { IsGuest = true, Tier = Shared.DecisionContractSDK.Enums.CustomerTier.STANDARD },
            ReasonCode = "HIGH_VALUE_NEED_APPROVAL",
            FallbackUsed = true,
            ApprovalExpiresAt = GovernanceTestData.Now.AddMinutes(30),
            CreatedAt = GovernanceTestData.Now,
        };
        var page = PagedResult<PendingApprovalItemDto>.Create([item], 2, 1, 5);

        var json = JsonSerializer.Serialize(page, TadssJson.Options);

        var results = ContractSchemas.Evaluate(ContractSchemas.PendingApprovals, json);
        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }
}
