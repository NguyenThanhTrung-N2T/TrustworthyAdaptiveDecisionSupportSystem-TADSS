using Shared.DecisionContractSDK.Common;
using TADSS.Application.Common;

namespace TADSS.Runtime.Api.Common;

/// <summary>
/// Đọc và kiểm tra header của contract (INTEGRATION_SPEC §3.2) cho controller: một nơi duy nhất cho quy tắc "thiếu/trắng → <c>MISSING_HEADER</c>,
/// quá dài → <c>VALIDATION_ERROR</c>". Lỗi ném <see cref="ContractValidationException"/> với trường là tên header để <c>GlobalExceptionHandler</c> dựng problem+json.
/// </summary>
internal static class RequestHeaders
{
    /// <summary>
    /// Đọc <c>X-Idempotency-Key</c> (bắt buộc, tối đa <see cref="ContractLimits.MaxIdempotencyKeyLength"/> ký tự).
    /// </summary>
    /// <param name="request">Request đang xử lý.</param>
    /// <returns>Giá trị header, giữ nguyên (không cắt khoảng trắng).</returns>
    /// <exception cref="ContractValidationException"><c>MISSING_HEADER</c> khi thiếu hoặc trắng; <c>VALIDATION_ERROR</c> khi quá dài.</exception>
    public static string RequireIdempotencyKey(HttpRequest request)
    {
        var value = Require(request, HttpHeaderNames.IdempotencyKey);
        if (value.Length > ContractLimits.MaxIdempotencyKeyLength)
        {
            throw TooLong(HttpHeaderNames.IdempotencyKey, ContractLimits.MaxIdempotencyKeyLength);
        }

        return value;
    }

    /// <summary>
    /// Đọc <c>X-Actor-Id</c> (bắt buộc, tối đa <see cref="ContractLimits.MaxActorIdLength"/> Unicode code point, khớp <c>varchar(100)</c> của PostgreSQL).
    /// </summary>
    /// <param name="request">Request đang xử lý.</param>
    /// <returns>Giá trị header, giữ nguyên (không cắt khoảng trắng).</returns>
    /// <exception cref="ContractValidationException"><c>MISSING_HEADER</c> khi thiếu hoặc trắng; <c>VALIDATION_ERROR</c> khi quá dài.</exception>
    public static string RequireActorId(HttpRequest request)
    {
        var value = Require(request, HttpHeaderNames.ActorId);
        if (value.EnumerateRunes().Count() > ContractLimits.MaxActorIdLength)
        {
            throw TooLong(HttpHeaderNames.ActorId, ContractLimits.MaxActorIdLength);
        }

        return value;
    }

    private static string Require(HttpRequest request, string headerName)
    {
        var value = request.Headers[headerName].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ContractValidationException(ErrorCodes.MissingHeader, $"Thiếu header {headerName}.", headerName);
        }

        return value;
    }

    private static ContractValidationException TooLong(string headerName, int maxLength) =>
        new(ErrorCodes.ValidationError, $"Header {headerName} dài tối đa {maxLength} ký tự.", headerName);
}
