using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Cấu hình JSON duy nhất của contract v2, dùng chung cho API, audit payload và test.
/// Enum và thời gian được kiểm tra chặt bằng converter gắn trên kiểu/thuộc tính, nên cấu hình này
/// <b>không</b> được thêm <see cref="JsonStringEnumConverter"/> toàn cục (converter toàn cục ghi đè converter trên kiểu).
/// </summary>
public static class TadssJson
{
    /// <summary>Bản cấu hình chỉ đọc, dùng trực tiếp khi serialize/deserialize.</summary>
    public static JsonSerializerOptions Options { get; } = CreateReadOnlyOptions();

    /// <summary>
    /// Áp cấu hình contract v2 lên một <see cref="JsonSerializerOptions"/> có sẵn (ví dụ options của ASP.NET Core).
    /// </summary>
    /// <param name="options">Options cần cấu hình.</param>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
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
