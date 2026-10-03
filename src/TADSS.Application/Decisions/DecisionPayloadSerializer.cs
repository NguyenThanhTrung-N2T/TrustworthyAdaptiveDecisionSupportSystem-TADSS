using System.Text.Json;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Application.Decisions;

/// <summary>
/// Serialize/deserialize <c>request_payload</c> và <c>response_payload</c> của bản ghi audit bằng <see cref="TadssJson.Options"/>
/// (nơi duy nhất làm việc này: composer ghi, replay và tra cứu đọc). Thuần, không trạng thái. Payload JSON <c>null</c> hoặc hỏng
/// thì <see cref="JsonException"/> lan ra thay vì bị che.
/// </summary>
public static class DecisionPayloadSerializer
{
    private const string RequestColumn = "request_payload";
    private const string ResponseColumn = "response_payload";

    /// <summary>Serialize request thành nội dung cột <c>request_payload</c>.</summary>
    public static string SerializeRequest(DecisionRequestDto request) =>
        JsonSerializer.Serialize(request, TadssJson.Options);

    /// <summary>Serialize response thành nội dung cột <c>response_payload</c>.</summary>
    public static string SerializeResponse(DecisionResultDto response) =>
        JsonSerializer.Serialize(response, TadssJson.Options);

    /// <summary>Đọc lại request từ <c>request_payload</c>.</summary>
    /// <exception cref="JsonException">Khi payload hỏng hoặc là JSON <c>null</c>.</exception>
    public static DecisionRequestDto DeserializeRequest(string payload) =>
        JsonSerializer.Deserialize<DecisionRequestDto>(payload, TadssJson.Options)
            ?? throw NullPayload(RequestColumn);

    /// <summary>Đọc lại response từ <c>response_payload</c>.</summary>
    /// <exception cref="JsonException">Khi payload hỏng hoặc là JSON <c>null</c>.</exception>
    public static DecisionResultDto DeserializeResponse(string payload) =>
        DeserializeResponseOrNull(payload) ?? throw NullPayload(ResponseColumn);

    /// <summary>
    /// Như <see cref="DeserializeResponse"/> nhưng trả <c>null</c> (thay vì ném) khi payload là JSON <c>null</c>, để nơi gọi giữ exception riêng
    /// của mình (replay ném <see cref="InvalidOperationException"/>). Payload hỏng vẫn ném <see cref="JsonException"/>.
    /// </summary>
    internal static DecisionResultDto? DeserializeResponseOrNull(string payload) =>
        JsonSerializer.Deserialize<DecisionResultDto>(payload, TadssJson.Options);

    private static JsonException NullPayload(string column) =>
        new($"Cột {column} của bản ghi audit là JSON null.");
}
