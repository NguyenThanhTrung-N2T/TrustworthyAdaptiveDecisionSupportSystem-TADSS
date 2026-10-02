using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Tính <c>request_hash</c> v2 (DECISION_PERSISTENCE_INVARIANTS C.2, bất biến INV-3): SHA-256 của chuỗi JSON canonical
/// gồm các trường nghiệp vụ theo thứ tự cố định, tất định và không phụ thuộc <see cref="CultureInfo.CurrentCulture"/>
/// hay múi giờ máy chủ. Không gồm <c>requestedAt</c>, <c>metadata</c>, <c>demand</c> (ảnh chụp nhu cầu đổi giữa các
/// lần retry cùng idempotency key). Danh sách trường được khai báo tường minh trong <see cref="WriteCanonical"/>;
/// thêm/bớt/đổi thứ tự trường là thay đổi INV-3 có chủ đích (mọi hash đã lưu sẽ lệch).
/// </summary>
/// <remarks>
/// Quy tắc chuẩn hóa: thời gian quy về UTC dạng <c>yyyy-MM-ddTHH:mm:ssZ</c> (bỏ phần lẻ giây); GUID dạng <c>D</c> chữ
/// thường; giá trị vắng (<c>null</c>) ghi JSON <c>null</c> và giữ khóa; enum ghi đúng tên wire; số tiền và số đếm ghi
/// thành chuỗi theo culture bất biến, bỏ số 0 vô nghĩa ở phần thập phân; <c>items</c> sắp xếp theo B-07 (thời gian bắt
/// đầu, thời gian kết thúc, <c>resourceId</c>, <c>resourceTypeId</c>, <c>price</c>) để đổi thứ tự item không đổi hash.
/// </remarks>
public static class RequestHasher
{
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// Tính <c>request_hash</c> của một request v2.
    /// </summary>
    /// <param name="request">Request đã qua deserialize/kiểm tra hợp đồng.</param>
    /// <returns>SHA-256 của canonical JSON (UTF-8) dưới dạng 64 ký tự hex thường.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> là <c>null</c>.</exception>
    public static string ComputeHash(DecisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, request);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    /// <summary>Ghi canonical theo thứ tự trường cố định; đây là danh sách trường duy nhất của hash.</summary>
    private static void WriteCanonical(Utf8JsonWriter writer, DecisionRequestDto request)
    {
        writer.WriteStartObject();
        writer.WriteString("reservationId", Format(request.ReservationId));
        writer.WriteString("branchId", Format(request.BranchId));
        writer.WriteString("branchTimeZone", request.BranchTimeZone);

        writer.WriteStartArray("items");
        foreach (var item in OrderItems(request.Items))
        {
            writer.WriteStartObject();
            writer.WriteString("resourceId", item.ResourceId);
            writer.WriteString("resourceTypeId", item.ResourceTypeId);
            writer.WriteString("startTime", item.StartTime);
            writer.WriteString("endTime", item.EndTime);
            writer.WriteString("price", Format(item.Price));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        WriteCustomer(writer, request.Customer);

        writer.WriteString("totalPrice", Format(request.TotalPrice));
        writer.WriteString("currency", request.Currency);
        writer.WriteString("channel", request.Channel.ToString());
        writer.WriteEndObject();
    }

    private static void WriteCustomer(Utf8JsonWriter writer, CustomerDto customer)
    {
        writer.WriteStartObject("customer");
        writer.WriteBoolean("isGuest", customer.IsGuest);
        WriteNullableString(writer, "customerId", customer.CustomerId is { } customerId ? Format(customerId) : null);
        writer.WriteString("tier", customer.Tier.ToString());
        WriteNullableString(writer, "memberSince", customer.MemberSince is { } memberSince ? Format(memberSince) : null);

        if (customer.History is { } history)
        {
            writer.WriteStartObject("history");
            writer.WriteString("completedCount", Format(history.CompletedCount));
            writer.WriteString("noShowCount", Format(history.NoShowCount));
            writer.WriteString("cancelledCount", Format(history.CancelledCount));
            writer.WriteString("lateCancelledCount", Format(history.LateCancelledCount));
            writer.WriteString("paymentExpiredCount", Format(history.PaymentExpiredCount));
            WriteNullableString(writer, "lastCompletedAt", history.LastCompletedAt is { } lastCompletedAt ? Format(lastCompletedAt) : null);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("history");
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    /// <summary>
    /// Sắp xếp item theo B-07. Mọi khóa đã ở dạng canonical (chuỗi thời gian UTC đến giây sắp xếp đúng theo thứ tự thời
    /// gian; GUID chữ thường so ordinal; giá tiền so theo giá trị) nên hai item cùng canonical là giống hệt nhau và thứ
    /// tự giữa chúng không ảnh hưởng hash.
    /// </summary>
    private static IEnumerable<CanonicalItem> OrderItems(IReadOnlyList<ReservationItemDto> items) =>
        items
            .Select(item => new CanonicalItem(
                Format(item.ResourceId),
                Format(item.ResourceTypeId),
                Format(item.StartTime),
                Format(item.EndTime),
                item.Price))
            .OrderBy(item => item.StartTime, StringComparer.Ordinal)
            .ThenBy(item => item.EndTime, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceTypeId, StringComparer.Ordinal)
            .ThenBy(item => item.Price);

    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();

    private static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Số thập phân ghi theo <see cref="CanonicalNumberFormatter"/> (quy tắc dùng chung với fingerprint strategy).</summary>
    private static string Format(decimal value) => CanonicalNumberFormatter.Format(value);

    /// <summary>Item đã chuẩn hóa; <see cref="Price"/> giữ dạng số chỉ để so sánh khi sắp xếp.</summary>
    private sealed record CanonicalItem(string ResourceId, string ResourceTypeId, string StartTime, string EndTime, decimal Price);
}
