using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Strategies;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Application.Strategies;

/// <summary>
/// UT-13 / B-08: <see cref="OptionsFingerprintBuilder"/> và fingerprint của hai strategy v2 tất định — không phụ thuộc
/// culture, thứ tự khai báo cửa sổ cao điểm hay thang số thập phân; đổi bất kỳ tham số hoặc lịch cao điểm nào đều đổi
/// fingerprint (để làm mất hiệu lực cache).
/// </summary>
public class OptionsFingerprintTests
{
    private static readonly string[] DefaultKeys =
    [
        "HighValueThreshold", "NoShowReviewThreshold", "StandardDepositRate", "SilverDepositRate",
        "GoldDepositRate", "PremiumDepositRate", "CacheTtlMinutes"
    ];

    private static readonly string[] MockKeys = ["HighValueThreshold", "PeakDepositRate", "OffPeakDepositRate", "CacheTtlMinutes"];

    /// <summary>Giá trị khác mặc định, hợp lệ cho từng tham số.</summary>
    private static readonly Dictionary<string, string> ChangedValues = new()
    {
        ["HighValueThreshold"] = "2500000",
        ["NoShowReviewThreshold"] = "3",
        ["StandardDepositRate"] = "0.55",
        ["SilverDepositRate"] = "0.45",
        ["GoldDepositRate"] = "0.35",
        ["PremiumDepositRate"] = "0.25",
        ["PeakDepositRate"] = "0.6",
        ["OffPeakDepositRate"] = "0.35",
        ["CacheTtlMinutes"] = "30",
    };

    public static TheoryData<string, string> StrategyParameters()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in DefaultKeys)
        {
            data.Add("Default", key);
        }

        foreach (var key in MockKeys)
        {
            data.Add("MockAlternative", key);
        }

        return data;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static string Create(string strategy, IConfiguration configuration, BranchCalendar? calendar = null) =>
        strategy == "Default"
            ? StrategyRegistrations.CreateDefault(configuration, calendar ?? DomainTestData.StandardCalendar()).OptionsFingerprint
            : StrategyRegistrations.CreateMockAlternative(configuration, calendar ?? DomainTestData.StandardCalendar()).OptionsFingerprint;

    private static BranchCalendar Calendar(params (DayOfWeek Day, int StartHour, int EndHour)[] windows) =>
        new(windows.Select(window => new PeakWindow(window.Day, new TimeOnly(window.StartHour, 0), new TimeOnly(window.EndHour, 0))));

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Fingerprint_SameOptionsDifferentCultures_Same()
    {
        var configuration = Config(
            ("Tadss:Strategies:Default:HighValueThreshold", "3000000.50"),
            ("Tadss:Strategies:Default:GoldDepositRate", "0.35"),
            ("Tadss:Strategies:MockAlternative:PeakDepositRate", "0.55"));

        var original = CultureInfo.CurrentCulture;
        var fingerprints = new List<string>();
        try
        {
            foreach (var name in new[] { "en-US", "vi-VN", "de-DE", "fr-FR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                fingerprints.Add(Create("Default", configuration) + "|" + Create("MockAlternative", configuration));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        fingerprints.Distinct().Should().ContainSingle();
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [MemberData(nameof(StrategyParameters))]
    public void Fingerprint_EachParameterChanged_Differs_Theory(string strategy, string parameter)
    {
        var baseline = Create(strategy, Config());

        var changed = Create(strategy, Config(($"Tadss:Strategies:{strategy}:{parameter}", ChangedValues[parameter])));

        changed.Should().NotBe(baseline, "đổi {0} của {1} phải đổi fingerprint", parameter, strategy);
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Default")]
    [InlineData("MockAlternative")]
    public void Fingerprint_CalendarChanged_Differs_Theory(string strategy)
    {
        var configuration = Config();
        var baseline = Create(strategy, configuration, Calendar((DayOfWeek.Monday, 17, 21)));

        Create(strategy, configuration, Calendar((DayOfWeek.Monday, 17, 22))).Should().NotBe(baseline, "đổi giờ kết thúc");
        Create(strategy, configuration, Calendar((DayOfWeek.Monday, 16, 21))).Should().NotBe(baseline, "đổi giờ bắt đầu");
        Create(strategy, configuration, Calendar((DayOfWeek.Tuesday, 17, 21))).Should().NotBe(baseline, "đổi ngày");
        Create(strategy, configuration, Calendar((DayOfWeek.Monday, 17, 21), (DayOfWeek.Tuesday, 17, 21))).Should().NotBe(baseline, "thêm cửa sổ");
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Default")]
    [InlineData("MockAlternative")]
    public void Fingerprint_CalendarOrder_Irrelevant_Theory(string strategy)
    {
        var configuration = Config();
        var forward = Calendar((DayOfWeek.Monday, 17, 21), (DayOfWeek.Saturday, 8, 21), (DayOfWeek.Sunday, 8, 21));
        var shuffled = Calendar((DayOfWeek.Sunday, 8, 21), (DayOfWeek.Monday, 17, 21), (DayOfWeek.Saturday, 8, 21));

        Create(strategy, configuration, shuffled).Should().Be(Create(strategy, configuration, forward));
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Default")]
    [InlineData("MockAlternative")]
    public void Fingerprint_Format_Is16LowercaseHex_Theory(string strategy)
    {
        Create(strategy, Config()).Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Default")]
    [InlineData("MockAlternative")]
    public void Fingerprint_EquivalentDecimalScale_Same_Theory(string strategy)
    {
        var plain = Create(strategy, Config(($"Tadss:Strategies:{strategy}:HighValueThreshold", "3000000")));
        var scaled = Create(strategy, Config(($"Tadss:Strategies:{strategy}:HighValueThreshold", "3000000.00")));

        scaled.Should().Be(plain);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Build_KnownInput_MatchesIndependentSha256()
    {
        var builder = new OptionsFingerprintBuilder();
        builder.Add("Rate", 0.50m);
        builder.Add("Threshold", 3000000.00m);
        builder.Add("Ttl", 60);

        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Rate=0.5\nThreshold=3000000\nTtl=60")))
            .ToLowerInvariant()[..16];

        builder.Build().Should().Be(expected);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void Build_ParameterOrderChanged_Differs()
    {
        var first = new OptionsFingerprintBuilder();
        first.Add("A", 1);
        first.Add("B", 2);
        var second = new OptionsFingerprintBuilder();
        second.Add("B", 2);
        second.Add("A", 1);

        second.Build().Should().NotBe(first.Build());
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("0.50", "0.5")]
    [InlineData("1.0", "1")]
    [InlineData("0.00", "0")]
    [InlineData("-0.0", "0")]
    public void Add_Decimal_DropsInsignificantTrailingZeros_Theory(string left, string right)
    {
        var a = new OptionsFingerprintBuilder();
        a.Add("X", decimal.Parse(left, CultureInfo.InvariantCulture));
        var b = new OptionsFingerprintBuilder();
        b.Add("X", decimal.Parse(right, CultureInfo.InvariantCulture));

        a.Build().Should().Be(b.Build());
    }
}
