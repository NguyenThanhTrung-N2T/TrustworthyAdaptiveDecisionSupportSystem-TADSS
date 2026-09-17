using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Runtime.Api.Services;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Services;

public class CrossCultureInvariantTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("fr-FR")]
    public void DecisionRequestSerialization_IsInvariantAcrossServerCultures(string cultureName)
    {
        var request = DecisionRequestFactory.ValidDecisionRequest();
        request.TotalPrice = 300000.50m;
        request.DepositAmount = 150000.25m;

        var json = WithCulture(cultureName, () => JsonSerializer.Serialize(request, JsonOptions));

        json.Should().Contain("\"totalPrice\":300000.5");
        json.Should().Contain("\"depositAmount\":150000.25");
        json.Should().NotContain("300000,5");
        json.Should().NotContain("150000,25");
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("fr-FR")]
    public void RequestHash_IsInvariantAcrossServerCultures(string cultureName)
    {
        var request = DecisionRequestFactory.ValidDecisionRequest();
        request.TotalPrice = 300000.50m;
        request.DepositAmount = 150000.25m;

        var invariantHash = WithCulture("en-US", () => RequestHasher.ComputeHash(request));
        var localizedHash = WithCulture(cultureName, () => RequestHasher.ComputeHash(request));

        localizedHash.Should().Be(invariantHash);
    }

    private static T WithCulture<T>(string cultureName, Func<T> action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
