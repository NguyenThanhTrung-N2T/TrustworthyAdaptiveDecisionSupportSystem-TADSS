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

    /// <summary>Độ dài tối đa của <c>X-Actor-Id</c> (Unicode code point), khớp cột <c>actor_id varchar(100)</c>.</summary>
    public const int MaxActorIdLength = 100;

    /// <summary>Độ dài tối thiểu của <c>reason</c> khi override (Unicode code point).</summary>
    public const int MinReasonLength = 5;

    /// <summary>Độ dài tối đa của <c>reason</c> khi override (Unicode code point).</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Kích thước trang mặc định của hàng chờ duyệt.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Kích thước trang tối đa của hàng chờ duyệt.</summary>
    public const int MaxPageSize = 100;
}
