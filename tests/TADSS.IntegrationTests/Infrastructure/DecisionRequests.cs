using System.Globalization;
using System.Text.Json.Nodes;
using TADSS.UnitTests.TestData;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Dựng body request v2 cho integration test từ payload mẫu của INTEGRATION_SPEC §3.3 (<c>ContractSamples</c>, dùng lại qua <c>Compile Link</c>).
/// Request mẫu: khách GOLD, tổng 228.000 VND, bắt đầu 20/11/2026 18:00 (+07:00), strategy mặc định cho <c>APPROVED</c> cọc 30%.
/// </summary>
public static class DecisionRequests
{
    /// <summary>Giá trị tổng tiền khiến <c>DefaultDecisionStrategy</c> trả <c>NEED_APPROVAL</c> (đạt ngưỡng giá trị lớn 2.000.000).</summary>
    public const decimal HighValueTotalPrice = 2_400_000m;

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Request mẫu hợp lệ.</summary>
    public static string Valid() => ContractSamples.RequestJson;

    /// <summary>Request mẫu sau khi áp <paramref name="mutate"/> lên JSON gốc.</summary>
    public static string Mutated(Action<JsonObject> mutate) => ContractSamples.MutateRequest(mutate);

    /// <summary>
    /// Request giá trị lớn nên strategy mặc định trả <c>NEED_APPROVAL</c>; item duy nhất bắt đầu tại <paramref name="start"/> và kéo dài 2 giờ.
    /// </summary>
    public static string HighValue(DateTimeOffset start) => Mutated(root =>
    {
        var item = ContractSamples.Object(root["items"]?[0]);
        item["startTime"] = Format(start);
        item["endTime"] = Format(start.AddHours(2));
        item["price"] = HighValueTotalPrice + 100_000m;
        root["totalPrice"] = HighValueTotalPrice;
    });

    /// <summary>Request mẫu nhưng item duy nhất bắt đầu tại <paramref name="start"/> và kéo dài 2 giờ (dùng khi test chạy bằng đồng hồ thật và cần giờ bắt đầu ở tương lai).</summary>
    public static string StartingAt(DateTimeOffset start) => Mutated(root =>
    {
        var item = ContractSamples.Object(root["items"]?[0]);
        item["startTime"] = Format(start);
        item["endTime"] = Format(start.AddHours(2));
    });

    /// <summary>Định dạng instant thành chuỗi ISO 8601 có offset +07:00 tường minh, theo yêu cầu của schema.</summary>
    public static string Format(DateTimeOffset value) =>
        value.ToOffset(VietnamOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
