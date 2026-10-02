using System.Text.Json;
using System.Text.Json.Nodes;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.UnitTests.TestData;

/// <summary>Payload mẫu của INTEGRATION_SPEC §3.3 và các hàm biến đổi để dựng ca sai.</summary>
internal static class V2Samples
{
    public const string RequestJson = """
        {
          "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
          "branchId": "1f2e3d4c-0000-4000-8000-000000000001",
          "branchTimeZone": "Asia/Ho_Chi_Minh",
          "items": [
            {
              "resourceId": "c0a8012e-0000-4000-8000-0000000000aa",
              "resourceTypeId": "c0a8012e-0000-4000-8000-0000000000bb",
              "startTime": "2026-11-20T18:00:00+07:00",
              "endTime": "2026-11-20T20:00:00+07:00",
              "price": 240000
            }
          ],
          "customer": {
            "customerId": "9d3f1c2a-0000-4000-8000-000000000123",
            "isGuest": false,
            "tier": "GOLD",
            "memberSince": "2025-03-14T08:00:00+07:00",
            "history": {
              "completedCount": 12,
              "noShowCount": 1,
              "cancelledCount": 2,
              "lateCancelledCount": 1,
              "paymentExpiredCount": 0,
              "lastCompletedAt": "2026-11-02T20:00:00+07:00"
            }
          },
          "totalPrice": 228000,
          "currency": "VND",
          "channel": "ONLINE",
          "demand": { "occupancyRate": 0.75, "waitlistCount": 2 },
          "requestedAt": "2026-11-18T09:15:00+07:00",
          "metadata": { "promotionCode": "WINTER" }
        }
        """;

    public const string ResultJson = """
        {
          "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
          "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
          "status": "APPROVED",
          "deposit": { "rate": 0.5, "amount": 114000, "currency": "VND" },
          "strategyName": "MockAlternativeDecisionStrategy",
          "strategyVersion": "2.0.0",
          "executionType": "AUTOMATED",
          "fallbackUsed": false,
          "reasonCode": "PEAK_HOUR_DEPOSIT",
          "reasons": ["Khung giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 50%."],
          "recommendations": [],
          "approvalExpiresAt": null,
          "evaluatedAt": "2026-11-18T02:15:00.041Z"
        }
        """;

    public static string MutateRequest(Action<JsonObject> mutate) => Mutate(RequestJson, mutate);

    public static string MutateResult(Action<JsonObject> mutate) => Mutate(ResultJson, mutate);

    public static DecisionRequestDto DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<DecisionRequestDto>(json, TadssJson.Options)
            ?? throw new InvalidOperationException("Deserialize trả null.");

    /// <summary>Bỏ null có kiểm tra: ném lỗi rõ ràng thay vì để assertion '?.' bị bỏ qua âm thầm.</summary>
    public static T Required<T>(T? value)
        where T : class =>
        value ?? throw new InvalidOperationException($"Giá trị {typeof(T).Name} bắt buộc nhưng là null.");

    /// <summary>Ép một nút JSON về object (ném lỗi rõ ràng nếu không phải object).</summary>
    public static JsonObject Object(JsonNode? node) =>
        node?.AsObject() ?? throw new InvalidOperationException("Nút JSON không phải object.");

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = Object(JsonNode.Parse(json));
        mutate(root);
        return root.ToJsonString();
    }
}
