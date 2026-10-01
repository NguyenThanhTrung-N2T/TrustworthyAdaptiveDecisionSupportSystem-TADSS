using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Converter JSON cho enum của contract v2: chỉ nhận đúng tên đã định nghĩa (phân biệt hoa thường),
/// không nhận số hay chuỗi số. <see cref="JsonStringEnumConverter"/> của .NET nhận cả <c>"1"</c> và chữ thường
/// nên không dùng được cho contract. Gắn trên từng enum bằng <see cref="JsonConverterAttribute"/>.
/// </summary>
internal sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)(Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))
            ?? throw new InvalidOperationException($"Không tạo được converter cho {typeToConvert}."));
}

/// <summary>Converter enum chặt cho một kiểu enum cụ thể.</summary>
internal sealed class StrictEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ValuesByName =
        Enum.GetValues<TEnum>().ToDictionary(value => value.ToString(), value => value, StringComparer.Ordinal);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Giá trị của {typeof(TEnum).Name} phải là chuỗi.");
        }

        var name = reader.GetString();
        if (name is null || !ValuesByName.TryGetValue(name, out var value))
        {
            throw new JsonException($"'{name}' không phải giá trị hợp lệ của {typeof(TEnum).Name}.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException($"Giá trị {value} không được định nghĩa trong {typeof(TEnum).Name}.");
        }

        writer.WriteStringValue(value.ToString());
    }
}
