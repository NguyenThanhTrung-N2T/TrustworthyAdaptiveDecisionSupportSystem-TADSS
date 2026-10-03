using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using TADSS.Application.Decisions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-06 / INV-3: <c>request_hash</c> v2 (<see cref="RequestHasher"/>) tất định — cùng yêu cầu nghiệp vụ cho cùng hash
/// bất kể offset, culture, thang số thập phân, thứ tự item hay các trường bị loại (<c>requestedAt</c>, <c>metadata</c>,
/// <c>demand</c>); đổi bất kỳ trường nghiệp vụ nào cho hash khác.
/// </summary>
public class RequestHasherTests
{
    /// <summary>
    /// Giá trị ghim của mẫu §3.3. Đổi giá trị này về sau là thay đổi INV-3 có chủ đích (mọi hash đã lưu sẽ lệch).
    /// Được tính độc lập với hiện thực: SHA-256 của <see cref="SpecSampleCanonical"/>.
    /// </summary>
    private const string SpecSamplePinnedHash = "0ec760d298c84fe652cc148f66002da5a3f74a258a1f962fe1f90602ff23d36a";

    /// <summary>Chuỗi canonical dựng tay (không qua <see cref="RequestHasher"/>) của mẫu §3.3.</summary>
    private const string SpecSampleCanonical =
        "{\"reservationId\":\"5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11\","
        + "\"branchId\":\"1f2e3d4c-0000-4000-8000-000000000001\","
        + "\"branchTimeZone\":\"Asia/Ho_Chi_Minh\","
        + "\"items\":[{\"resourceId\":\"c0a8012e-0000-4000-8000-0000000000aa\","
        + "\"resourceTypeId\":\"c0a8012e-0000-4000-8000-0000000000bb\","
        + "\"startTime\":\"2026-11-20T11:00:00Z\",\"endTime\":\"2026-11-20T13:00:00Z\",\"price\":\"240000\"}],"
        + "\"customer\":{\"isGuest\":false,\"customerId\":\"9d3f1c2a-0000-4000-8000-000000000123\","
        + "\"tier\":\"GOLD\",\"memberSince\":\"2025-03-14T01:00:00Z\","
        + "\"history\":{\"completedCount\":\"12\",\"noShowCount\":\"1\",\"cancelledCount\":\"2\","
        + "\"lateCancelledCount\":\"1\",\"paymentExpiredCount\":\"0\",\"lastCompletedAt\":\"2026-11-02T13:00:00Z\"}},"
        + "\"totalPrice\":\"228000\",\"currency\":\"VND\",\"channel\":\"ONLINE\"}";

    private static string Hash(string requestJson) => RequestHasher.ComputeHash(ContractSamples.DeserializeRequest(requestJson));

    private static string SampleHash() => Hash(ContractSamples.RequestJson);

    private static JsonObject FirstItem(JsonObject root) =>
        ContractSamples.Object(Items(root)[0]);

    private static JsonObject Customer(JsonObject root) => ContractSamples.Object(root["customer"]);

    private static JsonObject History(JsonObject root) => ContractSamples.Object(Customer(root)["history"]);

    private static JsonArray Items(JsonObject root) =>
        root["items"]?.AsArray() ?? throw new InvalidOperationException("Thiếu mảng items.");

    private static JsonObject ParseObject(string json) => ContractSamples.Object(JsonNode.Parse(json));

    /// <summary>Item thứ hai khác item mẫu (sân khác, khung giờ khác) để thử thứ tự.</summary>
    private static JsonObject SecondItem() => ParseObject("""
        {
          "resourceId": "c0a8012e-0000-4000-8000-0000000000ac",
          "resourceTypeId": "c0a8012e-0000-4000-8000-0000000000bb",
          "startTime": "2026-11-20T20:00:00+07:00",
          "endTime": "2026-11-20T21:00:00+07:00",
          "price": 120000
        }
        """);

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_SpecSample_MatchesPinnedValue()
    {
        SampleHash().Should().Be(SpecSamplePinnedHash);
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_SpecSample_EqualsSha256OfHandBuiltCanonical()
    {
        // Không dùng RequestHasher: khóa giá trị ghim vào chuỗi canonical viết tay nên không vòng tròn.
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SpecSampleCanonical))).ToLowerInvariant();

        expected.Should().Be(SpecSamplePinnedHash);
        SampleHash().Should().Be(expected);
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_SameInstantDifferentOffsets_SameHash()
    {
        var plus7 = ContractSamples.MutateRequest(root =>
        {
            FirstItem(root)["startTime"] = "2026-11-20T18:00:00+07:00";
            FirstItem(root)["endTime"] = "2026-11-20T20:00:00+07:00";
        });
        var utc = ContractSamples.MutateRequest(root =>
        {
            FirstItem(root)["startTime"] = "2026-11-20T11:00:00Z";
            FirstItem(root)["endTime"] = "2026-11-20T13:00:00Z";
        });
        var minus5 = ContractSamples.MutateRequest(root =>
        {
            FirstItem(root)["startTime"] = "2026-11-20T06:00:00-05:00";
            FirstItem(root)["endTime"] = "2026-11-20T08:00:00-05:00";
        });

        Hash(plus7).Should().Be(Hash(utc)).And.Be(Hash(minus5));
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_SubSecondFractions_AreIgnored()
    {
        var withFraction = ContractSamples.MutateRequest(root =>
            FirstItem(root)["startTime"] = "2026-11-20T18:00:00.750+07:00");

        Hash(withFraction).Should().Be(SampleHash());
    }

    [Theory]
    [Trait("CatalogId", "UT-06")]
    [InlineData("vi-VN")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("")]
    public void ComputeHash_DifferentCultures_SameHash_Theory(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var fractionalRequest = ContractSamples.MutateRequest(root => root["totalPrice"] = 228000.5m);

            SetCulture(CultureInfo.InvariantCulture);
            var invariantSample = SampleHash();
            var invariantFractional = Hash(fractionalRequest);

            SetCulture(CultureInfo.GetCultureInfo(cultureName));

            SampleHash().Should().Be(invariantSample).And.Be(SpecSamplePinnedHash);
            Hash(fractionalRequest).Should().Be(invariantFractional);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void SetCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    [Theory]
    [Trait("CatalogId", "UT-06")]
    [InlineData("228000", "228000.00")]
    [InlineData("228000", "228000.0")]
    [InlineData("228000.00", "228000.0")]
    [InlineData("0", "0.00")]
    [InlineData("0.5", "0.50")]
    [InlineData("100", "1E+2")]
    [InlineData("0.5", "5E-1")]
    [InlineData("12345.678", "12345.67800")]
    public void ComputeHash_DecimalScale_SameHash_Theory(string left, string right)
    {
        Hash(WithTotalPrice(left)).Should().Be(Hash(WithTotalPrice(right)));
    }

    [Theory]
    [Trait("CatalogId", "UT-06")]
    [InlineData("100", "10")]
    [InlineData("0.5", "5")]
    [InlineData("0", "0.01")]
    [InlineData("1.05", "1.5")]
    public void ComputeHash_DecimalDifferentValue_DifferentHash_Theory(string left, string right)
    {
        Hash(WithTotalPrice(left)).Should().NotBe(Hash(WithTotalPrice(right)));
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_DecimalScaleOnItemPrice_SameHash()
    {
        var scaled = ContractSamples.MutateRequest(root => FirstItem(root)["price"] = JsonNode.Parse("240000.000"));

        Hash(scaled).Should().Be(SampleHash());
    }

    private static string WithTotalPrice(string literal) =>
        ContractSamples.MutateRequest(root => root["totalPrice"] = JsonNode.Parse(literal));

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_ItemOrder_SameHash()
    {
        var forward = ContractSamples.MutateRequest(root => Items(root).Add(SecondItem()));
        var reversed = ContractSamples.MutateRequest(root =>
        {
            var items = Items(root);
            var sampleItem = FirstItem(root).DeepClone();
            items.Clear();
            items.Add(SecondItem());
            items.Add(sampleItem);
        });

        Hash(forward).Should().Be(Hash(reversed));
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_ItemOrderWithTiesOnTime_SameHash()
    {
        // Cùng khung giờ: thứ tự chỉ phụ thuộc resourceId rồi price (khóa phụ) nên mọi hoán vị cùng hash.
        static JsonObject Twin(string resourceId, int price) => ParseObject($$"""
            {
              "resourceId": "{{resourceId}}",
              "resourceTypeId": "c0a8012e-0000-4000-8000-0000000000bb",
              "startTime": "2026-11-20T18:00:00+07:00",
              "endTime": "2026-11-20T20:00:00+07:00",
              "price": {{price}}
            }
            """);

        static string WithOrder(params JsonObject[] items) => ContractSamples.MutateRequest(root =>
        {
            var array = Items(root);
            array.Clear();
            foreach (var item in items)
            {
                array.Add(item.DeepClone());
            }
        });

        var a = Twin("c0a8012e-0000-4000-8000-0000000000a1", 100);
        var b = Twin("c0a8012e-0000-4000-8000-0000000000a2", 100);
        var c = Twin("c0a8012e-0000-4000-8000-0000000000a2", 90);

        var reference = Hash(WithOrder(a, b, c));

        Hash(WithOrder(c, b, a)).Should().Be(reference);
        Hash(WithOrder(b, a, c)).Should().Be(reference);
        Hash(WithOrder(b, c, a)).Should().Be(reference);
    }

    private static TheoryData<string> CasesOf(Dictionary<string, Action<JsonObject>> mutations)
    {
        var data = new TheoryData<string>();
        foreach (var name in mutations.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static readonly Dictionary<string, Action<JsonObject>> ExcludedMutations = new()
    {
        ["requestedAt-changed"] = root => root["requestedAt"] = "2030-01-01T00:00:00Z",
        ["metadata-changed"] = root => root["metadata"] = ParseObject("""{ "promotionCode": "OTHER", "extra": "x" }"""),
        ["metadata-removed"] = root => root.Remove("metadata"),
        ["demand-changed"] = root => root["demand"] = ParseObject("""{ "occupancyRate": 0.1, "waitlistCount": 9 }"""),
        ["demand-removed"] = root => root.Remove("demand"),
    };

    public static TheoryData<string> ExcludedFieldCases => CasesOf(ExcludedMutations);

    [Theory]
    [Trait("CatalogId", "UT-06")]
    [MemberData(nameof(ExcludedFieldCases))]
    public void ComputeHash_ExcludedFields_SameHash_Theory(string caseName)
    {
        Hash(ContractSamples.MutateRequest(ExcludedMutations[caseName])).Should().Be(SampleHash());
    }

    private static readonly Dictionary<string, Action<JsonObject>> BusinessMutations = new()
    {
        ["reservationId"] = root => root["reservationId"] = "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a12",
        ["branchId"] = root => root["branchId"] = "1f2e3d4c-0000-4000-8000-000000000002",
        ["branchTimeZone"] = root => root["branchTimeZone"] = "Asia/Bangkok",
        ["item.resourceId"] = root => FirstItem(root)["resourceId"] = "c0a8012e-0000-4000-8000-0000000000ab",
        ["item.resourceTypeId"] = root => FirstItem(root)["resourceTypeId"] = "c0a8012e-0000-4000-8000-0000000000bc",
        ["item.startTime"] = root => FirstItem(root)["startTime"] = "2026-11-20T18:30:00+07:00",
        ["item.endTime"] = root => FirstItem(root)["endTime"] = "2026-11-20T20:30:00+07:00",
        ["item.price"] = root => FirstItem(root)["price"] = 240001,
        ["items.added"] = root => Items(root).Add(SecondItem()),
        ["customer.isGuest"] = root => Customer(root)["isGuest"] = true,
        ["customer.customerId"] = root => Customer(root)["customerId"] = "9d3f1c2a-0000-4000-8000-000000000124",
        ["customer.customerId-null"] = root => Customer(root)["customerId"] = null,
        ["customer.tier"] = root => Customer(root)["tier"] = "SILVER",
        ["customer.memberSince"] = root => Customer(root)["memberSince"] = "2025-03-14T08:00:01+07:00",
        ["customer.memberSince-null"] = root => Customer(root)["memberSince"] = null,
        ["history.completedCount"] = root => History(root)["completedCount"] = 13,
        ["history.noShowCount"] = root => History(root)["noShowCount"] = 2,
        ["history.cancelledCount"] = root => History(root)["cancelledCount"] = 3,
        ["history.lateCancelledCount"] = root => History(root)["lateCancelledCount"] = 2,
        ["history.paymentExpiredCount"] = root => History(root)["paymentExpiredCount"] = 1,
        ["history.lastCompletedAt"] = root => History(root)["lastCompletedAt"] = "2026-11-02T20:00:01+07:00",
        ["history.lastCompletedAt-null"] = root => History(root)["lastCompletedAt"] = null,
        ["totalPrice"] = root => root["totalPrice"] = 228001,
        ["currency"] = root => root["currency"] = "USD",
        ["channel"] = root => root["channel"] = "WALK_IN",
    };

    public static TheoryData<string> BusinessFieldCases => CasesOf(BusinessMutations);

    [Theory]
    [Trait("CatalogId", "UT-06")]
    [MemberData(nameof(BusinessFieldCases))]
    public void ComputeHash_BusinessFieldChanged_DifferentHash_Theory(string caseName)
    {
        Hash(ContractSamples.MutateRequest(BusinessMutations[caseName])).Should().NotBe(SampleHash());
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_NullVersusZeroHistory_DifferentHash()
    {
        var nullHistory = ContractSamples.MutateRequest(root => Customer(root)["history"] = null);
        var zeroHistory = ContractSamples.MutateRequest(root => Customer(root)["history"] = ParseObject("""
            {
              "completedCount": 0,
              "noShowCount": 0,
              "cancelledCount": 0,
              "lateCancelledCount": 0,
              "paymentExpiredCount": 0
            }
            """));

        Hash(nullHistory).Should().NotBe(Hash(zeroHistory));
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_OmittedOptionalCountEqualsExplicitZero_SameHash()
    {
        // paymentExpiredCount tùy chọn, mặc định 0: vắng mặt và ghi 0 là cùng một yêu cầu nghiệp vụ.
        var omitted = ContractSamples.MutateRequest(root => History(root).Remove("paymentExpiredCount"));

        Hash(omitted).Should().Be(SampleHash());
    }

    [Fact]
    [Trait("CatalogId", "UT-06")]
    public void ComputeHash_Output_Is64LowercaseHex()
    {
        SampleHash().Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
