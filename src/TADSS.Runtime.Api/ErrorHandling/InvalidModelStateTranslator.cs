using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Shared.DecisionContractSDK.Common;

namespace TADSS.Runtime.Api.ErrorHandling;

/// <summary>
/// Đường duy nhất cho lỗi giải mã/gán body của MVC (JSON hỏng, body rỗng, thiếu trường, trường lạ, enum sai, thời gian thiếu offset,
/// kiểu sai): chuyển <c>ModelState</c> không hợp lệ thành <see cref="ContractValidationException"/>
/// (<c>VALIDATION_ERROR</c>) rồi ném, để <c>GlobalExceptionHandler</c> dựng đúng một dạng RFC 7807 (kèm <c>traceId</c> = correlation id)
/// như mọi lỗi khác thay vì để MVC tự dựng <c>ValidationProblemDetails</c>.
/// Chỉ xử lý lỗi model-binding/giải mã JSON; Content-Type lạ (415) cố ý để MVC giữ mặc định
/// (<c>UnsupportedContentTypeFilter</c> chạy trước <c>ModelStateInvalidFilter</c>), <c>traceId</c> do <c>CustomizeProblemDetails</c> bổ sung.
/// Không bao giờ đưa <c>ModelError.ErrorMessage</c> hay <c>ModelError.Exception.Message</c> ra response (F-23): thông điệp của System.Text.Json lộ tên kiểu .NET,
/// <c>LineNumber</c>, <c>BytePositionInLine</c>. <c>detail</c> chỉ là câu cố định theo trường (đường dẫn JSON do chính client gửi).
/// Gắn vào <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c>.
/// </summary>
internal static class InvalidModelStateTranslator
{
    private const int MaxReportedErrors = 3;
    private const int MaxFieldLength = 100;
    private const string UnknownFieldDetail = "Request body không phải JSON hợp lệ theo contract.";

    /// <summary>Factory của <c>ApiBehaviorOptions</c>: luôn ném (không trả response).</summary>
    /// <exception cref="ContractValidationException">Luôn ném, mang mã <c>VALIDATION_ERROR</c>.</exception>
    public static IActionResult Throw(ActionContext context) =>
        throw Translate(context.ModelState);

    /// <summary>
    /// Dựng lỗi contract từ <paramref name="modelState"/>: trường là đường dẫn JSON của lỗi đầu tiên, detail gồm tối đa <see cref="MaxReportedErrors"/> câu cố định
    /// (mỗi trường một câu). Không xác định được trường (JSON hỏng, body rỗng) thì dùng câu chung.
    /// </summary>
    public static ContractValidationException Translate(ModelStateDictionary modelState)
    {
        // Lỗi giải mã JSON có khóa "$..." (hoặc rỗng); MVC còn thêm một mục trùng lặp theo tên tham số action ("request") nên bỏ nó khi đã có mục JSON
        var entries = modelState.Where(entry => entry.Value is { Errors.Count: > 0 }).ToList();
        if (entries.Any(entry => IsJsonLevelKey(entry.Key)))
        {
            entries = entries.Where(entry => IsJsonLevelKey(entry.Key)).ToList();
        }

        var errors = entries
            .SelectMany(entry => entry.Value?.Errors.Select(_ => (Field: NormalizeField(entry.Key), Message: SentenceOf(NormalizeField(entry.Key)))) ?? [])
            .DistinctBy(error => error.Message)
            .ToList();

        var detail = errors.Count == 0
            ? "Request body không hợp lệ."
            : "Request body không hợp lệ: " + string.Join(" ", errors.Take(MaxReportedErrors).Select(error => error.Message));

        return new ContractValidationException(ErrorCodes.ValidationError, detail, errors.Count == 0 ? null : errors[0].Field);
    }

    private static bool IsJsonLevelKey(string key) => key.Length == 0 || key.StartsWith('$');

    private static string? NormalizeField(string key)
    {
        var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        if (string.IsNullOrEmpty(field) || field == "$")
        {
            return null;
        }

        // Tên trường do client gửi (có thể rất dài): cắt để detail và khóa của errors không phình theo body
        return field.Length <= MaxFieldLength ? field : field[..MaxFieldLength];
    }

    private static string SentenceOf(string? field) =>
        field is null ? UnknownFieldDetail : $"Giá trị của trường '{field}' không hợp lệ.";
}
