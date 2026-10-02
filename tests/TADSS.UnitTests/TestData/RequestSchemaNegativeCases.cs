using System.Text.Json.Nodes;

namespace TADSS.UnitTests.TestData;

/// <summary>
/// Nguồn dữ liệu dùng chung cho mọi ca âm của <c>decision-request.schema.json</c>: mỗi ca là một biến đổi của payload mẫu
/// khiến schema từ chối. <c>DecisionSchemaTests</c> kiểm schema từ chối chúng; <c>RequestErrorTierTests</c> kiểm SDK cũng
/// không bao giờ chấp nhận chúng (CT-01, B-06).
/// </summary>
internal static class RequestSchemaNegativeCases
{
    private static readonly string[] DateTimeWithoutOffsetNames =
    [
        "start-without-offset",
        "end-without-offset",
        "requested-at-without-offset",
        "member-since-without-offset",
        "last-completed-without-offset",
        "requested-at-numeric-offset"
    ];

    private static readonly Dictionary<string, Action<JsonObject>> Mutations = new(StringComparer.Ordinal)
    {
        ["extra-root"] = root => root["unexpected"] = 1,
        ["start-without-offset"] = root => Item(root)["startTime"] = "2026-11-20T18:00:00",
        ["end-without-offset"] = root => Item(root)["endTime"] = "2026-11-20T20:00:00",
        ["requested-at-without-offset"] = root => root["requestedAt"] = "2026-11-18T09:15:00",
        ["member-since-without-offset"] = root => Customer(root)["memberSince"] = "2025-03-14T08:00:00",
        ["last-completed-without-offset"] = root => History(root)["lastCompletedAt"] = "2026-11-02T20:00:00",
        ["requested-at-numeric-offset"] = root => root["requestedAt"] = "2026-11-18T09:15:00+0700",
        ["channel-lowercase"] = root => root["channel"] = "online",
        ["tier-unknown"] = root => Customer(root)["tier"] = "BRONZE",
        ["reservation-id-not-uuid"] = root => root["reservationId"] = "not-a-uuid",
        ["currency-missing"] = root => root.Remove("currency"),
        ["branch-time-zone-empty"] = root => root["branchTimeZone"] = string.Empty,
        ["items-null"] = root => root["items"] = null,
        ["items-empty"] = root => root["items"] = new JsonArray(),
        ["items-too-many"] = root => root["items"] = ItemsWith(21, _ => { }),
        ["items-null-element"] = root => root["items"] = new JsonArray(null, null),
        ["items-negative-price"] = root => root["items"] = ItemsWith(3, item => item["price"] = -1),
        ["total-price-negative"] = root => root["totalPrice"] = -1,
        ["currency-null"] = root => root["currency"] = null,
        ["currency-lowercase"] = root => root["currency"] = "vnd",
        ["currency-four-letters"] = root => root["currency"] = "VNDD",
        ["customer-null"] = root => root["customer"] = null,
        ["member-without-customer-id"] = root => Customer(root).Remove("customerId"),
        ["member-null-customer-id"] = root => Customer(root)["customerId"] = null,
        ["guest-with-customer-id"] = root =>
        {
            var customer = Customer(root);
            customer.Remove("memberSince");
            customer["isGuest"] = true;
        },
        ["guest-with-member-since"] = root =>
        {
            var customer = Customer(root);
            customer.Remove("customerId");
            customer["isGuest"] = true;
        },
        ["history-completed-negative"] = root => History(root)["completedCount"] = -1,
        ["history-no-show-negative"] = root => History(root)["noShowCount"] = -1,
        ["history-cancelled-negative"] = root => History(root)["cancelledCount"] = -1,
        ["history-late-cancelled-negative"] = root => History(root)["lateCancelledCount"] = -1,
        ["history-payment-expired-negative"] = root => History(root)["paymentExpiredCount"] = -1,
        ["occupancy-above-one"] = root => Demand(root)["occupancyRate"] = 1.5,
        ["occupancy-below-zero"] = root => Demand(root)["occupancyRate"] = -0.1,
        ["waitlist-negative"] = root => Demand(root)["waitlistCount"] = -1,
        ["metadata-null-value"] = root => V2Samples.Object(root["metadata"])["promotionCode"] = null
    };

    /// <summary>Tên mọi ca âm (dùng với <c>MemberData</c>).</summary>
    public static IEnumerable<object[]> All => Mutations.Keys.Select(name => new object[] { name });

    /// <summary>Các ca thời gian thiếu offset tường minh — schema phải từ chối cả khi tắt kiểm <c>format</c>.</summary>
    public static IEnumerable<object[]> DateTimeWithoutExplicitOffset => DateTimeWithoutOffsetNames.Select(name => new object[] { name });

    /// <summary>Payload mẫu đã áp biến đổi của ca <paramref name="caseName"/>.</summary>
    public static string Json(string caseName) =>
        Mutations.TryGetValue(caseName, out var mutate)
            ? V2Samples.MutateRequest(mutate)
            : throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Không có ca âm này.");

    private static JsonObject Item(JsonObject root) => V2Samples.Object(root["items"]?[0]);

    private static JsonObject Customer(JsonObject root) => V2Samples.Object(root["customer"]);

    private static JsonObject History(JsonObject root) => V2Samples.Object(Customer(root)["history"]);

    private static JsonObject Demand(JsonObject root) => V2Samples.Object(root["demand"]);

    /// <summary>Mảng <paramref name="count"/> item sao từ item mẫu; <paramref name="mutateLast"/> sửa item cuối.</summary>
    private static JsonArray ItemsWith(int count, Action<JsonObject> mutateLast)
    {
        var sampleItem = Item(V2Samples.Object(JsonNode.Parse(V2Samples.RequestJson)));
        var items = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            items.Add(sampleItem.DeepClone());
        }

        mutateLast(V2Samples.Object(items[count - 1]));
        return items;
    }
}
