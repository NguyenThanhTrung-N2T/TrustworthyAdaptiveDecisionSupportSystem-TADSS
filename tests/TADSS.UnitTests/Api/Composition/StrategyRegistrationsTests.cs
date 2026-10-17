using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Strategies;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-04 (spec 4.2.2): <see cref="StrategyRegistrations"/> bind tham số từ <c>Tadss:Strategies:{Default|MockAlternative}</c>
/// vào options của strategy v2 (thuộc tính <c>init</c> phải bind được), thiếu section thì dùng mặc định của POCO,
/// tham số sai thì ném lỗi nêu tên section để ứng dụng không khởi động.
/// </summary>
public class StrategyRegistrationsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static async Task<DecisionStatus> DecideAsync(RegisteredStrategy registered, decimal totalPrice)
    {
        var reservation = DomainTestData.CreateReservation(totalPrice: totalPrice, customer: DomainTestData.CreateMember());
        var decision = await registered.Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);
        return decision.Status;
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task CreateDefault_ConfiguredValues_AreBound()
    {
        var registered = StrategyRegistrations.CreateDefault(
            Config(("Tadss:Strategies:Default:HighValueThreshold", "3000000")), DomainTestData.StandardCalendar());

        // Ngưỡng mới 3.000.000: đơn 2.500.000 không còn cần duyệt (mặc định 2.000.000 thì cần)
        (await DecideAsync(registered, 2_500_000m)).Should().Be(DecisionStatus.APPROVED);
        (await DecideAsync(registered, 3_000_000m)).Should().Be(DecisionStatus.NEED_APPROVAL);
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task CreateDefault_NoSection_UsesPocoDefaults()
    {
        var registered = StrategyRegistrations.CreateDefault(Config(), DomainTestData.StandardCalendar());

        (await DecideAsync(registered, 1_999_999m)).Should().Be(DecisionStatus.APPROVED);
        (await DecideAsync(registered, 2_500_000m)).Should().Be(DecisionStatus.NEED_APPROVAL);
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task CreateMockAlternative_ConfiguredValues_AreBound()
    {
        var calendar = DomainTestData.StandardCalendar();
        var defaults = StrategyRegistrations.CreateMockAlternative(Config(), calendar);
        var raised = StrategyRegistrations.CreateMockAlternative(
            Config(("Tadss:Strategies:MockAlternative:HighValueThreshold", "9000000")), calendar);

        // Đơn 2.000.000 vào giờ cao điểm (thứ Sáu 18:00): mặc định (1.500.000) cần duyệt, ngưỡng 9.000.000 thì không
        (await DecideAsync(defaults, 2_000_000m)).Should().Be(DecisionStatus.NEED_APPROVAL);
        (await DecideAsync(raised, 2_000_000m)).Should().Be(DecisionStatus.APPROVED);
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task CreateDefault_DecimalWithDotUnderOtherCulture_BindsInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var registered = StrategyRegistrations.CreateDefault(
                Config(("Tadss:Strategies:Default:HighValueThreshold", "3000000.5")), DomainTestData.StandardCalendar());

            (await DecideAsync(registered, 3_000_000m)).Should().Be(DecisionStatus.APPROVED);
            (await DecideAsync(registered, 3_000_001m)).Should().Be(DecisionStatus.NEED_APPROVAL);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("GoldDepositRate", "1.5")]
    [InlineData("NoShowReviewThreshold", "0")]
    [InlineData("HighValueThreshold", "-1")]
    [InlineData("CacheTtlMinutes", "-5")]
    public void CreateDefault_InvalidOptions_Throws_Theory(string key, string value)
    {
        var act = () => StrategyRegistrations.CreateDefault(
            Config(($"Tadss:Strategies:Default:{key}", value)), DomainTestData.StandardCalendar());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Tadss:Strategies:Default*")
            .WithInnerException<ArgumentException>();
    }

    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("PeakDepositRate", "2")]
    [InlineData("OffPeakDepositRate", "-0.1")]
    [InlineData("CacheTtlMinutes", "-1")]
    public void CreateMockAlternative_InvalidOptions_Throws_Theory(string key, string value)
    {
        var act = () => StrategyRegistrations.CreateMockAlternative(
            Config(($"Tadss:Strategies:MockAlternative:{key}", value)), DomainTestData.StandardCalendar());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Tadss:Strategies:MockAlternative*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void CreateDefault_NotANumber_ThrowsNamingSection()
    {
        var act = () => StrategyRegistrations.CreateDefault(
            Config(("Tadss:Strategies:Default:HighValueThreshold", "abc")), DomainTestData.StandardCalendar());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Tadss:Strategies:Default*");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Create_Strategies_ReportNamesAndVersion()
    {
        var calendar = DomainTestData.StandardCalendar();

        var defaultStrategy = StrategyRegistrations.CreateDefault(Config(), calendar);
        var mock = StrategyRegistrations.CreateMockAlternative(Config(), calendar);

        defaultStrategy.Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
        mock.Strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        defaultStrategy.Strategy.StrategyVersion.Should().Be("2.1.0");
        mock.Strategy.StrategyVersion.Should().Be("2.0.0");
    }
}
