using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Application.Decisions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-11: <see cref="DecisionPayloadSerializer"/> là nơi duy nhất (qua <see cref="TadssJson.Options"/>) biến request/response thành
/// <c>request_payload</c>/<c>response_payload</c> và ngược lại: từng byte phải trùng với serialize trực tiếp, payload null/hỏng ném
/// <see cref="JsonException"/>.
/// </summary>
public class DecisionPayloadSerializerTests
{
    public static TheoryData<string> UnreadablePayloads => new()
    {
        "null",
        "{ \"decisionId\": \"khong-phai-guid\" }",
        "{ không phải JSON"
    };

    private static DecisionRequestDto SampleRequest() => ContractSamples.DeserializeRequest(ContractSamples.RequestJson);

    private static DecisionResultDto SampleResponse() =>
        ContractSamples.Required(JsonSerializer.Deserialize<DecisionResultDto>(ContractSamples.ResultJson, TadssJson.Options));

    /// <summary>Serialize bằng serializer trùng từng byte với <c>JsonSerializer</c> + <see cref="TadssJson.Options"/>; deserialize khôi phục bằng nhau.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void Serializer_RoundTrip_PreservesBytesOfComposerOutput()
    {
        var request = SampleRequest();
        var response = SampleResponse();

        var requestPayload = DecisionPayloadSerializer.SerializeRequest(request);
        var responsePayload = DecisionPayloadSerializer.SerializeResponse(response);

        requestPayload.Should().Be(JsonSerializer.Serialize(request, TadssJson.Options));
        responsePayload.Should().Be(JsonSerializer.Serialize(response, TadssJson.Options));
        DecisionPayloadSerializer.DeserializeRequest(requestPayload).Should().BeEquivalentTo(request);
        DecisionPayloadSerializer.DeserializeResponse(responsePayload).Should().BeEquivalentTo(response);
        DecisionPayloadSerializer.SerializeResponse(DecisionPayloadSerializer.DeserializeResponse(responsePayload)).Should().Be(responsePayload);
    }

    /// <summary>Payload null hoặc hỏng không bị che: <see cref="JsonException"/> lan ra (như <c>DecisionQueryService</c> hiện tại).</summary>
    [Theory]
    [Trait("CatalogId", "UT-11")]
    [MemberData(nameof(UnreadablePayloads))]
    public void Deserialize_NullOrCorruptPayload_ThrowsJsonException_Theory(string payload)
    {
        var request = () => DecisionPayloadSerializer.DeserializeRequest(payload);
        var response = () => DecisionPayloadSerializer.DeserializeResponse(payload);

        request.Should().Throw<JsonException>();
        response.Should().Throw<JsonException>();
    }

    /// <summary>Biến thể không ném cho JSON null (replay giữ <see cref="InvalidOperationException"/> riêng); payload hỏng vẫn ném <see cref="JsonException"/>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public void DeserializeResponseOrNull_JsonNull_ReturnsNullButCorruptStillThrows()
    {
        DecisionPayloadSerializer.DeserializeResponseOrNull("null").Should().BeNull();

        var corrupt = () => DecisionPayloadSerializer.DeserializeResponseOrNull("{ không phải JSON");

        corrupt.Should().Throw<JsonException>();
    }
}
