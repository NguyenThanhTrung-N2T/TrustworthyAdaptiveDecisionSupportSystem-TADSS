namespace TADSS.Application.Common;

/// <summary>
/// Giới hạn do contract cố định (INTEGRATION_SPEC §2), dùng chung cho controller, service, middleware, cấu hình EF và tài liệu OpenAPI.
/// Mỗi giới hạn chỉ khai báo một nơi; đây là hằng của contract chứ không phải tham số vận hành nên không đưa vào cấu hình.
/// </summary>
public static class ContractLimits
{
    /// <summary>Độ dài tối đa của <c>X-Idempotency-Key</c> (ký tự).</summary>
    public const int MaxIdempotencyKeyLength = 128;

    /// <summary>Độ dài tối đa của correlation id được chấp nhận (ký tự); dài hơn thì bị thay bằng GUID mới (C-07).</summary>
    public const int MaxCorrelationIdLength = 128;
}
