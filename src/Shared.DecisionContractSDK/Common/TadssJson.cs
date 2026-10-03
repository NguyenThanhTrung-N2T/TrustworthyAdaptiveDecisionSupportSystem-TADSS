using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Cấu hình JSON duy nhất của contract v2, dùng chung cho API, audit payload và test.
/// Tên thuộc tính phân biệt hoa thường (camelCase), số phải là số JSON (không nhận chuỗi số), trường lạ bị từ chối.
/// Enum và thời gian được kiểm tra chặt bằng converter gắn trên kiểu/thuộc tính, nên cấu hình này
/// <b>không</b> được thêm <see cref="JsonStringEnumConverter"/> toàn cục (converter toàn cục ghi đè converter trên kiểu).
/// </summary>
public static class TadssJson
{
    /// <summary>Bản cấu hình chỉ đọc, dùng trực tiếp khi serialize/deserialize.</summary>
    public static JsonSerializerOptions Options { get; } = CreateReadOnlyOptions();

    /// <summary>
    /// Áp cấu hình contract v2 lên một <see cref="JsonSerializerOptions"/> có sẵn (ví dụ options của ASP.NET Core):
    /// camelCase, phân biệt hoa thường, số chặt (<see cref="JsonNumberHandling.Strict"/>), từ chối trường lạ.
    /// Ghi đè các giá trị lỏng hơn mà <see cref="JsonSerializerDefaults.Web"/> bật sẵn.
    /// </summary>
    /// <param name="options">Options cần cấu hình.</param>
    /// <exception cref="InvalidOperationException">
    /// Khi <paramref name="options"/> đã có <see cref="JsonStringEnumConverter"/> toàn cục (converter này ghi đè converter chặt gắn trên kiểu).
    /// </exception>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            throw new InvalidOperationException(
                "Options đã có JsonStringEnumConverter toàn cục, làm enum v2 nhận chữ thường và chuỗi số. " +
                "Hãy dùng options riêng cho contract v2 hoặc gỡ converter enum toàn cục trước khi gọi TadssJson.Apply.");
        }

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    }

    private static JsonSerializerOptions CreateReadOnlyOptions()
    {
        var options = new JsonSerializerOptions();
        Apply(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
