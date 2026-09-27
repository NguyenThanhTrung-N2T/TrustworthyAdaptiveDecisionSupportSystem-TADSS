using TADSS.BenchmarkRunner.Protocol;

namespace TADSS.BenchmarkRunner.Telemetry;

public static class ResultAggregator
{
    public static ScenarioSummaryMetrics Aggregate(
        RunMetadata metadata,
        IReadOnlyList<RawMeasurementRecord> records,
        double totalWallClockMs,
        int strategyInvocations,
        int observedRetryCount,
        int observedInnerTimeoutCount)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            throw new InvalidOperationException($"No measurement records collected for run {metadata.RunId}.");
        }

        if (totalWallClockMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalWallClockMs), "Total wall-clock time cannot be negative.");
        }

        int count = records.Count;
        var clientLatencies = new double[count];
        var serverLatencies = new double[count];

        int eligibleRequests = 0;
        int completedSafeDecisions = 0;
        int cacheHitCount = 0;
        int fallbackCount = 0;
        int httpErrorCount = 0;

        for (int i = 0; i < count; i++)
        {
            var r = records[i];
            clientLatencies[i] = r.ClientElapsedMs;
            serverLatencies[i] = r.ServerExecutionTimeMs;

            if (r.IsEligibleForDcr)
            {
                eligibleRequests++;
                if (r.IsSafeCompletedDecision)
                {
                    completedSafeDecisions++;
                }
            }

            if (r.CacheHit) cacheHitCount++;
            if (r.FallbackUsed) fallbackCount++;
            if (r.HttpStatusCode != 200) httpErrorCount++;
        }

        // Sort in-place to avoid extra LINQ OrderBy allocations
        Array.Sort(clientLatencies);
        Array.Sort(serverLatencies);

        if (eligibleRequests > 0 && completedSafeDecisions == 0)
        {
            Console.Error.WriteLine($"  [WARNING] Run {metadata.RunId} observed 0% DCR (100% request failure rate).");
        }

        double dcrPercent = eligibleRequests > 0
            ? Math.Round(completedSafeDecisions * 100.0 / eligibleRequests, 2)
            : 0.0;

        double cacheHitRatioPercent = Math.Round(cacheHitCount * 100.0 / count, 2);
        double fallbackRatePercent = Math.Round(fallbackCount * 100.0 / count, 2);
        double httpErrorRatePercent = Math.Round(httpErrorCount * 100.0 / count, 2);
        double throughputRps = totalWallClockMs > 0
            ? Math.Round(count / (totalWallClockMs / 1000.0), 2)
            : 0.0;

        string circuitStateObserved = metadata.Scenario switch
        {
            nameof(ExperimentScenarioId.R2) => strategyInvocations == 0 && fallbackCount == count
                ? "OPEN (Verified 0 Strategy Invocations)"
                : $"OPEN_PARTIAL ({strategyInvocations} Invocations)",

            nameof(ExperimentScenarioId.E4) => strategyInvocations > 0
                ? $"RECOVERED_CLOSED (Probe OK, {fallbackCount} Early Fallbacks)"
                : "STILL_OPEN",

            _ => "CLOSED"
        };

        return new ScenarioSummaryMetrics
        {
            Metadata = metadata,
            TotalWallClockMs = Math.Round(totalWallClockMs, 2),
            ThroughputRps = throughputRps,

            ClientP50Ms = Math.Round(ComputePercentile(clientLatencies, 0.50), 2),
            ClientP90Ms = Math.Round(ComputePercentile(clientLatencies, 0.90), 2),
            ClientP95Ms = Math.Round(ComputePercentile(clientLatencies, 0.95), 2),
            ClientP99Ms = Math.Round(ComputePercentile(clientLatencies, 0.99), 2),
            ClientMaxMs = Math.Round(clientLatencies[^1], 2),
            ClientMeanMs = Math.Round(ComputeMean(clientLatencies), 2),
            ClientStdDevMs = Math.Round(ComputeStdDev(clientLatencies), 2),

            ServerP50Ms = Math.Round(ComputePercentile(serverLatencies, 0.50), 2),
            ServerP90Ms = Math.Round(ComputePercentile(serverLatencies, 0.90), 2),
            ServerP95Ms = Math.Round(ComputePercentile(serverLatencies, 0.95), 2),
            ServerP99Ms = Math.Round(ComputePercentile(serverLatencies, 0.99), 2),
            ServerMaxMs = Math.Round(serverLatencies[^1], 2),
            ServerMeanMs = Math.Round(ComputeMean(serverLatencies), 2),

            EligibleRequests = eligibleRequests,
            CompletedSafeDecisions = completedSafeDecisions,
            DcrPercent = dcrPercent,
            CacheHitRatioPercent = cacheHitRatioPercent,
            FallbackRatePercent = fallbackRatePercent,
            HttpErrorOrTimeoutCount = httpErrorCount,
            HttpErrorRatePercent = httpErrorRatePercent,
            StrategyInvocations = strategyInvocations,
            ObservedRetryCount = observedRetryCount,
            ObservedInnerTimeoutCount = observedInnerTimeoutCount,
            CircuitStateObserved = circuitStateObserved
        };
    }

    public static void PopulateCacheDifferentials(IList<ScenarioSummaryMetrics> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var byConcurrency = summaries.GroupBy(s => s.Metadata.Concurrency);
        foreach (var group in byConcurrency)
        {
            var g0 = group.FirstOrDefault(s => s.Metadata.Scenario == nameof(ExperimentScenarioId.G0));
            var g1 = group.FirstOrDefault(s => s.Metadata.Scenario == nameof(ExperimentScenarioId.G1));
            var g2 = group.FirstOrDefault(s => s.Metadata.Scenario == nameof(ExperimentScenarioId.G2));
            var c1 = group.FirstOrDefault(s => s.Metadata.Scenario == nameof(ExperimentScenarioId.C1));

            if (g0 != null && g1 != null)
            {
                g1.CacheMissOverheadP50Ms = Math.Round(g1.ClientP50Ms - g0.ClientP50Ms, 2);
                g1.CacheMissOverheadP95Ms = Math.Round(g1.ClientP95Ms - g0.ClientP95Ms, 2);
            }

            if (g0 != null && c1 != null)
            {
                c1.CacheMissOverheadP50Ms = Math.Round(c1.ClientP50Ms - g0.ClientP50Ms, 2);
                c1.CacheMissOverheadP95Ms = Math.Round(c1.ClientP95Ms - g0.ClientP95Ms, 2);
            }

            if (g0 != null && g2 != null)
            {
                g2.CacheBenefitP50Ms = Math.Round(g0.ClientP50Ms - g2.ClientP50Ms, 2);
                g2.CacheBenefitP95Ms = Math.Round(g0.ClientP95Ms - g2.ClientP95Ms, 2);
            }
        }
    }

    public static double ComputePercentile(ReadOnlySpan<double> sortedValues, double percentile)
    {
        if (percentile is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be between 0.0 and 1.0.");
        }

        if (sortedValues.IsEmpty) return 0.0;
        if (sortedValues.Length == 1) return sortedValues[0];

        double position = (sortedValues.Length - 1) * percentile;
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        double weight = position - lowerIndex;
        return sortedValues[lowerIndex] * (1.0 - weight) + sortedValues[upperIndex] * weight;
    }

    public static double ComputeMean(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }

        return sum / values.Length;
    }

    public static double ComputeStdDev(ReadOnlySpan<double> values)
    {
        if (values.Length <= 1) return 0.0;
        double mean = ComputeMean(values);
        double sumSquares = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            double diff = values[i] - mean;
            sumSquares += diff * diff;
        }

        return Math.Sqrt(sumSquares / (values.Length - 1));
    }
}
