namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Machine-readable prefixes for failure reasons recorded in audit trail during resilience fallback execution.
/// Follows the failure taxonomy specified in docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md.
/// </summary>
public static class FailureReasonPrefixes
{
    /// <summary>Strategy execution exceeded the per-attempt timeout budget.</summary>
    public const string StrategyTimeout = "STRATEGY_TIMEOUT";

    /// <summary>Circuit breaker opened due to high failure rate, isolating the primary strategy.</summary>
    public const string CircuitBreakerOpen = "CIRCUIT_BREAKER_OPEN";

    /// <summary>Strategy threw an unhandled computational or integration exception.</summary>
    public const string StrategyException = "STRATEGY_EXCEPTION";

    /// <summary>Format a strategy timeout failure reason message.</summary>
    public static string FormatTimeout(int timeoutMs) =>
        $"{StrategyTimeout}: Strategy execution timed out (> {timeoutMs}ms per attempt).";

    /// <summary>Format a circuit breaker open failure reason message.</summary>
    public static string FormatCircuitBreakerOpen() =>
        $"{CircuitBreakerOpen}: Circuit breaker is open due to high failure rate. Strategy execution isolated.";

    /// <summary>Format a strategy exception failure reason message.</summary>
    public static string FormatException(string? message) =>
        $"{StrategyException}: {message ?? "Strategy execution failed with an unexpected error."}";
}
