using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Sdk.Common;

/// <summary>
/// UT-02: enum v2 trên wire chỉ nhận đúng tên đã định nghĩa (phân biệt hoa thường), không nhận số hay chuỗi số;
/// <see cref="TadssJson"/> không đăng ký converter enum toàn cục.
/// </summary>
public class WireEnumTests
{
    private static readonly Type[] V2EnumTypes =
    [
        typeof(DecisionStatus), typeof(ExecutionType), typeof(CustomerTier), typeof(Channel),
        typeof(OverrideAction), typeof(DecisionOutcome), typeof(RecommendationType)
    ];

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Deserialize_ExactName_ReturnsValue()
    {
        JsonSerializer.Deserialize<Channel>("\"WALK_IN\"", TadssJson.Options).Should().Be(Channel.WALK_IN);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("\"walk_in\"")]
    [InlineData("\"PHONE\"")]
    [InlineData("\" ONLINE\"")]
    [InlineData("null")]
    public void Deserialize_InvalidToken_ThrowsJsonException(string json)
    {
        var act = () => JsonSerializer.Deserialize<Channel>(json, TadssJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Serialize_DefinedValue_WritesExactName()
    {
        JsonSerializer.Serialize(Channel.WALK_IN, TadssJson.Options).Should().Be("\"WALK_IN\"");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Serialize_UndefinedValue_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Serialize((Channel)99, TadssJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void RoundTrip_EveryMemberOfEveryV2Enum_PreservesValue()
    {
        foreach (var enumType in V2EnumTypes)
        {
            foreach (var value in Enum.GetValues(enumType))
            {
                var json = JsonSerializer.Serialize(value, enumType, TadssJson.Options);

                json.Should().Be($"\"{value}\"");
                JsonSerializer.Deserialize(json, enumType, TadssJson.Options).Should().Be(value);
            }
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Options_IsReadOnlyCamelCaseWithoutGlobalConverters()
    {
        TadssJson.Options.IsReadOnly.Should().BeTrue();
        TadssJson.Options.Converters.Should().BeEmpty("converter enum toàn cục sẽ ghi đè converter chặt gắn trên kiểu");
        TadssJson.Options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        TadssJson.Options.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Disallow);
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Apply_ExistingOptions_RejectsUnmappedMembersAndKeepsStrictEnums()
    {
        var options = new JsonSerializerOptions();
        TadssJson.Apply(options);

        var parsed = JsonSerializer.Deserialize<ChannelProbe>("{\"channel\":\"ONLINE\"}", options)
            ?? throw new InvalidOperationException("Deserialize trả null.");
        var withExtra = () => JsonSerializer.Deserialize<ChannelProbe>("{\"channel\":\"ONLINE\",\"extra\":1}", options);
        var withNumericString = () => JsonSerializer.Deserialize<ChannelProbe>("{\"channel\":\"0\"}", options);

        parsed.Channel.Should().Be(Channel.ONLINE);
        withExtra.Should().Throw<JsonException>();
        withNumericString.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Options_IsStrictAboutPropertyCaseAndNumbers()
    {
        // Options là bản đã MakeReadOnly: ghi nhận giá trị thực tế đang áp dụng
        TadssJson.Options.PropertyNameCaseInsensitive.Should().BeFalse();
        TadssJson.Options.NumberHandling.Should().Be(JsonNumberHandling.Strict);
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Apply_AspNetWebOptions_StillAcceptsValidRequest()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        TadssJson.Apply(options);

        var dto = JsonSerializer.Deserialize<DecisionRequestDto>(ContractSamples.RequestJson, options);

        ContractSamples.Required(dto).TotalPrice.Should().Be(228_000m);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("property-wrong-case")]
    [InlineData("price-as-string")]
    [InlineData("channel-lowercase")]
    [InlineData("channel-number")]
    [InlineData("channel-numeric-string")]
    public void Apply_AspNetWebOptions_RejectsLooseRequestPayloads(string caseName)
    {
        // JsonSerializerDefaults.Web bật sẵn PropertyNameCaseInsensitive và AllowReadingFromString
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        TadssJson.Apply(options);
        var json = ContractSamples.MutateRequest(root =>
        {
            switch (caseName)
            {
                case "property-wrong-case": root.Remove("totalPrice"); root["TotalPrice"] = 228000; break;
                case "price-as-string": root["totalPrice"] = "228000"; break;
                case "channel-lowercase": root["channel"] = "online"; break;
                case "channel-number": root["channel"] = 1; break;
                case "channel-numeric-string": root["channel"] = "1"; break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        var act = () => JsonSerializer.Deserialize<DecisionRequestDto>(json, options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Apply_OptionsWithGlobalEnumConverter_ThrowsInvalidOperation()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var act = () => TadssJson.Apply(options);

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class ChannelProbe
    {
        public Channel Channel { get; set; }
    }
}
