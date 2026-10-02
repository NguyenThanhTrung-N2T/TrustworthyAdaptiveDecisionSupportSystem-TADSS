using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Shared.DecisionContractSDK.Common;

namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Đường duy nhất cho lỗi giải mã/gán body của MVC (JSON hỏng, body rỗng, thiếu trường, trường lạ, enum sai, thời gian thiếu offset,
/// kiểu sai, Content-Type lạ): chuyển <c>ModelState</c> không hợp lệ thành <see cref="ContractValidationException"/>
/// (<c>VALIDATION_ERROR</c>) rồi ném, để <c>GlobalExceptionHandler</c> dựng đúng một dạng RFC 7807 (kèm <c>traceId</c> = correlation id)
/// như mọi lỗi khác thay vì để MVC tự dựng <c>ValidationProblemDetails</c>/415 có dạng riêng.
/// Gắn vào <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c>.
/// </summary>
internal static class InvalidModelStateTranslator
{
    private const int MaxReportedErrors = 3;
    private const int MaxMessageLength = 300;

    /// <summary>Factory của <c>ApiBehaviorOptions</c>: luôn ném (không trả response).</summary>
    /// <exception cref="ContractValidationException">Luôn ném, mang mã <c>VALIDATION_ERROR</c>.</exception>
    public static IActionResult Throw(ActionContext context) =>
        throw Translate(context.ModelState);

    /// <summary>Dựng lỗi contract từ <paramref name="modelState"/>: trường là đường dẫn JSON của lỗi đầu tiên, detail gồm tối đa vài lỗi.</summary>
    public static ContractValidationException Translate(ModelStateDictionary modelState)
    {
        var errors = modelState
            .SelectMany(entry => entry.Value?.Errors.Select(error => (Field: NormalizeField(entry.Key), Message: MessageOf(error))) ?? [])
            .ToList();

        var detail = errors.Count == 0
            ? "Request body không hợp lệ."
            : "Request body không hợp lệ: " + string.Join(" ", errors.Take(MaxReportedErrors).Select(error => Truncate(error.Message)));

        return new ContractValidationException(ErrorCodes.ValidationError, detail, errors.Count == 0 ? null : errors[0].Field);
    }

    private static string? NormalizeField(string key)
    {
        var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return string.IsNullOrEmpty(field) || field == "$" ? null : field;
    }

    private static string MessageOf(ModelError error) =>
        !string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? error.ErrorMessage
            : error.Exception?.Message ?? "Giá trị không hợp lệ.";

    private static string Truncate(string message) =>
        message.Length <= MaxMessageLength ? message : message[..MaxMessageLength] + "...";
}
