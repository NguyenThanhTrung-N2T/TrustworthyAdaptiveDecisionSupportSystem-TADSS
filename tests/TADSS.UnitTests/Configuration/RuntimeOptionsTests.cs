using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Services.Decisions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Configuration;

/// <summary>
/// Cấu hình runtime v2 (<c>Tadss:Approval</c>, <c>Tadss:Validation</c>, <c>Tadss:Calendar</c>): giá trị mặc định
/// trong <c>appsettings.json</c>, cơ chế thay thế (không nối) danh sách cao điểm và kiểm tra fail-fast khi khởi động.
/// </summary>
public class RuntimeOptionsTests
{
    private static readonly (DayOfWeek Day, TimeOnly Start, TimeOnly End)[] SasDefaultWindows =
    [
        (DayOfWeek.Monday, new TimeOnly(17, 0), new TimeOnly(21, 0)),
        (DayOfWeek.Tuesday, new TimeOnly(17, 0), new TimeOnly(21, 0)),
        (DayOfWeek.Wednesday, new TimeOnly(17, 0), new TimeOnly(21, 0)),
        (DayOfWeek.Thursday, new TimeOnly(17, 0), new TimeOnly(21, 0)),
        (DayOfWeek.Friday, new TimeOnly(17, 0), new TimeOnly(21, 0)),
        (DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(21, 0)),
        (DayOfWeek.Sunday, new TimeOnly(8, 0), new TimeOnly(21, 0)),
    ];

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Bind_DefaultConfiguration_UsesSpecDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();
        using var provider = BuildProvider(configuration);

        var approval = provider.GetRequiredService<IOptions<ApprovalOptions>>().Value;
        var validation = provider.GetRequiredService<IOptions<ValidationOptions>>().Value;
        var calendar = provider.GetRequiredService<BranchCalendar>();

        approval.WindowMinutes.Should().Be(30);
        approval.MinLeadMinutes.Should().Be(120);
        validation.MinItemMinutes.Should().Be(30);
        validation.MaxItemMinutes.Should().Be(720);
        Project(calendar).Should().Equal(SasDefaultWindows, "appsettings.json phải khai báo đúng lịch cao điểm SAS 4.3");
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Bind_PeakWindowsOverridden_ReplacesDefaults()
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Calendar:PeakWindows:0:Day", "Wednesday"),
            ("Tadss:Calendar:PeakWindows:0:Start", "06:30"),
            ("Tadss:Calendar:PeakWindows:0:End", "09:00")));

        var calendar = provider.GetRequiredService<BranchCalendar>();

        Project(calendar).Should().Equal([(DayOfWeek.Wednesday, new TimeOnly(6, 30), new TimeOnly(9, 0))]);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Bind_NoCalendarSection_FallsBackToDefaults()
    {
        using var provider = BuildProvider(InMemory());

        var calendar = provider.GetRequiredService<BranchCalendar>();

        Project(calendar).Should().Equal(SasDefaultWindows);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void DefaultPeakWindows_MatchStandardCalendarOfTests()
    {
        var calendar = new CalendarOptions().ToCalendar();

        Project(calendar).Should().Equal(Project(DomainTestData.StandardCalendar()));
        CalendarOptions.DefaultPeakWindows.Should().HaveCount(7);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData("60", "30")]
    [InlineData("0", "720")]
    [InlineData("-5", "720")]
    public void Validate_InvalidItemLimits_FailsWithP01_Theory(string min, string max)
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Validation:MinItemMinutes", min),
            ("Tadss:Validation:MaxItemMinutes", max)));

        var act = () => provider.GetRequiredService<IOptions<ValidationOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*P-01*");
    }

    [Theory]
    [Trait("CatalogId", "UT-10")]
    [InlineData("0", "120")]
    [InlineData("-1", "120")]
    [InlineData("30", "-1")]
    public void Validate_InvalidApprovalOptions_Fails_Theory(string window, string lead)
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Approval:WindowMinutes", window),
            ("Tadss:Approval:MinLeadMinutes", lead)));

        var act = () => provider.GetRequiredService<IOptions<ApprovalOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Validate_ApprovalWindowZero_Fails()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Approval:WindowMinutes", "0")));

        var act = () => provider.GetRequiredService<IOptions<ApprovalOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*WindowMinutes*");
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void Validate_ApprovalMinLeadZero_IsAccepted()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Approval:MinLeadMinutes", "0")));

        provider.GetRequiredService<IOptions<ApprovalOptions>>().Value.MinLeadMinutes.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Validate_PeakWindowBadFormat_Fails()
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Calendar:PeakWindows:0:Day", "Monday"),
            ("Tadss:Calendar:PeakWindows:0:Start", "17:00"),
            ("Tadss:Calendar:PeakWindows:0:End", "21:00"),
            ("Tadss:Calendar:PeakWindows:1:Day", "Tuesday"),
            ("Tadss:Calendar:PeakWindows:1:Start", "7pm"),
            ("Tadss:Calendar:PeakWindows:1:End", "21:00")));

        var act = () => provider.GetRequiredService<BranchCalendar>();

        act.Should().Throw<OptionsValidationException>().WithMessage("*PeakWindows[1]*");
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Validate_PeakWindowMissingDay_FailsInsteadOfBindingSunday()
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Calendar:PeakWindows:0:Start", "17:00"),
            ("Tadss:Calendar:PeakWindows:0:End", "21:00")));

        var act = () => provider.GetRequiredService<BranchCalendar>();

        act.Should().Throw<OptionsValidationException>().WithMessage("*PeakWindows[0].Day*");
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Validate_PeakWindowStartAfterEnd_FailsWithoutLeakingDomainException()
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Calendar:PeakWindows:0:Day", "Monday"),
            ("Tadss:Calendar:PeakWindows:0:Start", "21:00"),
            ("Tadss:Calendar:PeakWindows:0:End", "17:00")));

        var act = () => provider.GetRequiredService<IOptions<CalendarOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*PeakWindows[0]*");
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Validate_PeakWindowStartEqualsEnd_Fails()
    {
        using var provider = BuildProvider(InMemory(
            ("Tadss:Calendar:PeakWindows:0:Day", "Monday"),
            ("Tadss:Calendar:PeakWindows:0:Start", "17:00"),
            ("Tadss:Calendar:PeakWindows:0:End", "17:00")));

        var act = () => provider.GetRequiredService<IOptions<CalendarOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*PeakWindows[0]*");
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void AddDecisionRuntime_DefaultConfiguration_ResolvesRegisteredServices()
    {
        using var provider = BuildProvider(InMemory());

        provider.GetRequiredService<BranchCalendar>().PeakWindows.Should().HaveCount(7);
        provider.GetRequiredService<ItemDurationPolicy>().Should().NotBeNull();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    private static IConfiguration InMemory(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddDecisionRuntime(configuration);
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<(DayOfWeek Day, TimeOnly Start, TimeOnly End)> Project(BranchCalendar calendar) =>
        calendar.PeakWindows.Select(window => (window.Day, window.Start, window.End)).ToList();
}
