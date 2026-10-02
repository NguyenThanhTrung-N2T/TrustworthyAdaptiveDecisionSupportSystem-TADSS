using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Converter thời gian của contract v2: chỉ nhận ISO 8601 có offset tường minh (<c>Z</c> hoặc <c>±hh:mm</c>).
/// Thời gian thiếu offset bị từ chối thay vì để .NET gán múi giờ của máy chủ (INV-3).
/// Gắn trên thuộc tính <see cref="DateTimeOffset"/> hoặc <see cref="Nullable{DateTimeOffset}"/>.
/// </summary>
internal sealed class OffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    private static readonly Regex OffsetPattern = new(
        @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Thời gian phải là chuỗi ISO 8601 có offset.");
        }

        var text = reader.GetString();
        if (text is null
            || !OffsetPattern.IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new JsonException($"'{text}' không phải thời gian ISO 8601 có offset (ví dụ 2026-11-20T18:00:00+07:00).");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
