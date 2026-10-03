namespace TADSS.Runtime.Api.Common;

/// <summary>
/// Canonical HTTP Header names used across the TADSS Decision Runtime API.
/// </summary>
public static class HttpHeaderNames
{
    /// <summary>Mandatory idempotency key header for decision evaluation requests.</summary>
    public const string IdempotencyKey = "X-Idempotency-Key";

    /// <summary>Mandatory manager identifier header for override requests (set by SmashCourt from the authenticated JWT).</summary>
    public const string ActorId = "X-Actor-Id";

    /// <summary>Service-to-service API key issued by TADSS per client; required on every <c>/api/*</c> request.</summary>
    public const string ApiKey = "X-Api-Key";

    /// <summary>Optional canonical correlation identifier for end-to-end distributed tracing.</summary>
    public const string CorrelationId = "X-Correlation-ID";
}
