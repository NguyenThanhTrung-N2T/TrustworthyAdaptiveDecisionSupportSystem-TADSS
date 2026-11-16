using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Shared.DecisionContractSDK.Common;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-04: <see cref="StrategyRegistrations.CreateDefault"/> với chính sách giảm cọc giờ thấp điểm — bind đúng khóa của bản deploy (kể cả danh sách hạng dạng
/// biến môi trường <c>__0</c>, <c>__1</c>); khóa lạ hoặc giá trị sai làm runtime không khởi động (fail-fast, không bỏ qua âm thầm); dấu vân tay tham số đổi
/// khi đổi bất kỳ tham số giảm cọc nào, không đổi khi chỉ đổi thứ tự khai báo hạng, và chính dấu vân tay đó được ghi vào lý do của quyết định được giảm.
/// </summary>
public class DefaultStrategyDiscountRegistrationTests
{
    private const string Section = "Tadss:Strategies:Default";

    private static readonly (string Key, string Value)[] DeployKeys =
    [
        ($"{Section}:OffPeakDiscountEnabled", "true"),
        ($"{Section}:OffPeakDiscountRate", "0.3"),
        ($"{Section}:OffPeakDiscountTiers:0", "STANDARD"),
        ($"{Section}:OffPeakDiscountTiers:1", "SILVER"),
    ];

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static string FingerprintOf(params (string Key, string Value)[] values) =>
        StrategyRegistrations.CreateDefault(Config(values), DomainTestData.StandardCalendar()).OptionsFingerprint;

    /// <summary>Cấu hình của bản deploy bind đủ: khách STANDARD giờ thấp điểm cọc 30 %, lý do mang đúng dấu vân tay của strategy đã đăng ký.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task CreateDefault_DeployDiscountKeys_AreBoundAndFingerprintIsTraced()
    {
        var registered = StrategyRegistrations.CreateDefault(Config(DeployKeys), DomainTestData.StandardCalendar());
        var item = DomainTestData.CreateItem(DateTimeOffset.Parse("2026-11-20T10:00:00+07:00", CultureInfo.InvariantCulture));
        var reservation = DomainTestData.CreateReservation(
            items: [item], customer: DomainTestData.CreateMember(Shared.DecisionContractSDK.Enums.CustomerTier.STANDARD, DomainTestData.CreateHistory()));

        var decision = await registered.Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.DepositRate.Should().Be(0.3m);
        decision.ReasonCode.Should().Be(ReasonCodes.OffPeakDeposit);
        registered.OptionsFingerprint.Should().MatchRegex("^[0-9a-f]{16}$");
        decision.Reasons.Should().ContainSingle().Which.Should().EndWith($"cấu hình strategy {registered.OptionsFingerprint}.");
    }

    /// <summary>Khóa gõ sai (ví dụ <c>OffPeakDiscountEnable</c>) hoặc không tồn tại: không khởi động, thông báo nêu tên section.</summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("Tadss:Strategies:Default:OffPeakDiscountEnable", "true")]
    [InlineData("Tadss:Strategies:Default:OffPeakDiscountTier:0", "STANDARD")]
    [InlineData("Tadss:Strategies:MockAlternative:PeakRate", "0.5")]
    public void Create_UnknownKey_ThrowsNamingSection(string key, string value)
    {
        var configuration = Config((key, value));

        var act = () =>
        {
            StrategyRegistrations.CreateDefault(configuration, DomainTestData.StandardCalendar());
            StrategyRegistrations.CreateMockAlternative(configuration, DomainTestData.StandardCalendar());
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*Tadss:Strategies:*");
    }

    /// <summary>Giá trị sai (bật mà không có hạng, hạng lạ, tỷ lệ ngoài miền): không khởi động.</summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("OffPeakDiscountTiers:0", "VIP")]
    [InlineData("OffPeakDiscountRate", "1.5")]
    [InlineData("OffPeakDiscountMaxNoShowCount", "-1")]
    public void CreateDefault_InvalidDiscountValue_Throws(string key, string value)
    {
        var values = DeployKeys.Where(pair => !pair.Key.StartsWith($"{Section}:{key.Split(':')[0]}", StringComparison.Ordinal))
            .Append(($"{Section}:{key}", value)).ToArray();

        var act = () => StrategyRegistrations.CreateDefault(Config(values), DomainTestData.StandardCalendar());

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{Section}*");
    }

    /// <summary>Bật mà danh sách hạng rỗng: không khởi động.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void CreateDefault_EnabledWithoutTiers_Throws()
    {
        var act = () => StrategyRegistrations.CreateDefault(Config(($"{Section}:OffPeakDiscountEnabled", "true")), DomainTestData.StandardCalendar());

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{Section}*OffPeakDiscountTiers*");
    }

    /// <summary>Dấu vân tay ổn định (cùng tham số ⇒ cùng giá trị) và đổi khi đổi bất kỳ tham số giảm cọc nào, kể cả khi chỉ bật / tắt.</summary>
    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("OffPeakDiscountEnabled", "false")]
    [InlineData("OffPeakDiscountRate", "0.35")]
    [InlineData("OffPeakDiscountTiers:2", "GOLD")]
    [InlineData("OffPeakDiscountMaxNoShowCount", "1")]
    [InlineData("OffPeakDiscountMaxPaymentExpiredCount", "1")]
    public void CreateDefault_ChangingAnyDiscountParameter_ChangesFingerprint(string key, string value)
    {
        var changed = DeployKeys.Where(pair => pair.Key != $"{Section}:{key}").Append(($"{Section}:{key}", value)).ToArray();

        FingerprintOf(DeployKeys).Should().Be(FingerprintOf(DeployKeys));
        FingerprintOf(changed).Should().NotBe(FingerprintOf(DeployKeys));
    }

    /// <summary>Thứ tự khai báo hạng không đổi hành vi nên không đổi dấu vân tay; thêm khóa giảm cọc khác mặc định thì đổi so với khi không cấu hình.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void CreateDefault_TierOrder_DoesNotChangeFingerprint()
    {
        var reordered = new[]
        {
            ($"{Section}:OffPeakDiscountEnabled", "true"),
            ($"{Section}:OffPeakDiscountRate", "0.3"),
            ($"{Section}:OffPeakDiscountTiers:0", "SILVER"),
            ($"{Section}:OffPeakDiscountTiers:1", "STANDARD"),
        };

        FingerprintOf(reordered).Should().Be(FingerprintOf(DeployKeys));
        FingerprintOf(DeployKeys).Should().NotBe(FingerprintOf());
    }
}
