using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
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
        "{ \"reservationId\": \"khong-phai-guid\" }",
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

    /// <summary>
    /// Bản ghi cũ có trường lạ (DTO về sau bỏ trường) vẫn đọc được: trường lạ ở gốc và ở object lồng (<c>customer</c>) bị bỏ qua,
    /// các giá trị còn lại giữ nguyên.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void DeserializeRequest_UnknownProperty_IsIgnored()
    {
        var json = ContractSamples.MutateRequest(root =>
        {
            root["truongLaOGoc"] = "x";
            ContractSamples.Object(root["customer"])["truongLaTrongCustomer"] = 7;
        });

        var request = DecisionPayloadSerializer.DeserializeRequest(json);

        request.Should().BeEquivalentTo(SampleRequest());
    }

    /// <summary>Bản ghi cũ thiếu một trường <c>required</c> (DTO thêm trường sau) vẫn đọc được; trường thiếu mang giá trị mặc định.</summary>
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void DeserializeResponse_MissingRequiredProperty_UsesDefault()
    {
        var json = ContractSamples.MutateResult(root => root.Remove("reasonCode"));

        var response = DecisionPayloadSerializer.DeserializeResponse(json);

        response.ReasonCode.Should().BeNull();
        response.Status.Should().Be(DecisionStatus.APPROVED);
        response.StrategyName.Should().Be(SampleResponse().StrategyName);
    }

    /// <summary>Đọc nới lỏng chỉ áp cho trường lạ/thiếu: enum chữ thường và thời gian thiếu offset vẫn bị từ chối.</summary>
    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("status", "approved")]
    [InlineData("evaluatedAt", "2026-11-18T02:15:00.041")]
    public void DeserializeRequest_StrictEnumAndOffset_StillEnforced_Theory(string property, string value)
    {
        var json = ContractSamples.MutateResult(root => root[property] = value);

        var read = () => DecisionPayloadSerializer.DeserializeResponse(json);

        read.Should().Throw<JsonException>();
    }

    /// <summary>Đường ghi giữ options chặt: kết quả serialize trùng từng byte với <see cref="TadssJson.Options"/>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void SerializeRequest_UsesStrictOptions()
    {
        var request = SampleRequest();

        DecisionPayloadSerializer.SerializeRequest(request)
            .Should().Be(JsonSerializer.Serialize(request, TadssJson.Options));
        DecisionPayloadSerializer.SerializeResponse(SampleResponse())
            .Should().Be(JsonSerializer.Serialize(SampleResponse(), TadssJson.Options));
    }
}
