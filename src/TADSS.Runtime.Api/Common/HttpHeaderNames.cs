namespace TADSS.Runtime.Api.Common;

/// <summary>
/// Canonical HTTP Header names used across the TADSS Decision Runtime API.
/// </summary>
public static class HttpHeaderNames
{
    /// <summary>Mandatory idempotency key header for decision evaluation requests.</summary>
    public const string IdempotencyKey = "X-Idempotency-Key";

    /// <summary>Optional canonical correlation identifier for end-to-end distributed tracing.</summary>
    public const string CorrelationId = "X-Correlation-ID";
}
