using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Outcomes;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Ảnh chụp một response HTTP đã đọc hết body: giữ cả byte thô (so sánh "giống từng byte") lẫn chuỗi, để test không phải đọc lại
/// <see cref="HttpContent"/> và response không bị giải phóng giữa chừng.
/// </summary>
/// <param name="Status">Mã trạng thái HTTP.</param>
/// <param name="Headers">Header của response.</param>
/// <param name="ContentType">Media type của body (<c>null</c> khi không có body).</param>
/// <param name="Bytes">Body thô.</param>
public sealed record DecisionHttpResponse(HttpStatusCode Status, HttpResponseHeaders Headers, string? ContentType, byte[] Bytes)
{
    /// <summary>Body dạng chuỗi UTF-8.</summary>
    public string Body => Encoding.UTF8.GetString(Bytes);

    /// <summary>Body của response 200 giải mã thành <see cref="DecisionResultDto"/> (<c>POST</c>).</summary>
    public DecisionResultDto Result => Deserialize<DecisionResultDto>();

    /// <summary>Body của response 200 giải mã thành <see cref="DecisionDetailDto"/> (<c>GET</c>).</summary>
    public DecisionDetailDto Detail => Deserialize<DecisionDetailDto>();

    /// <summary>Body của response 200 giải mã thành <see cref="OverrideResponseDto"/> (<c>override</c>).</summary>
    public OverrideResponseDto Override => Deserialize<OverrideResponseDto>();

    /// <summary>Body của response 200 giải mã thành <see cref="OutcomeResponseDto"/> (<c>outcome</c>).</summary>
    public OutcomeResponseDto Outcome => Deserialize<OutcomeResponseDto>();

    /// <summary>Body của response 200 giải mã thành một trang hàng chờ duyệt (<c>pending-approvals</c>).</summary>
    public PagedResult<PendingApprovalItemDto> Pending => Deserialize<PagedResult<PendingApprovalItemDto>>();

    /// <summary>Body của response lỗi (problem+json) dưới dạng <see cref="JsonElement"/>.</summary>
    public JsonElement Problem
    {
        get
        {
            using var document = JsonDocument.Parse(Bytes);
            return document.RootElement.Clone();
        }
    }

    /// <summary>Giá trị của header <paramref name="name"/> hoặc <c>null</c> khi không có.</summary>
    public string? Header(string name) => Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private T Deserialize<T>() =>
        JsonSerializer.Deserialize<T>(Bytes, TadssJson.Options)
        ?? throw new InvalidOperationException($"Body không giải mã được thành {typeof(T).Name}.");
}
