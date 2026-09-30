using System.Diagnostics;
using TADSS.BenchmarkRunner.Export;
using TADSS.BenchmarkRunner.Harness;
using TADSS.BenchmarkRunner.Normalization;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.BenchmarkRunner.Telemetry;

namespace TADSS.BenchmarkRunner;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Any(a => a is "--help" or "-h"))
        {
            Console.WriteLine(CliArgumentParser.GetUsageText());
            return 0;
        }

        BenchmarkSuiteConfig config;
        try
        {
            config = CliArgumentParser.ParseAndValidate(args);
        }
        catch (ArgumentException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            await Console.Error.WriteLineAsync($"[INVALID ARGUMENTS] {ex.Message}");
            Console.ResetColor();
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(CliArgumentParser.GetUsageText());
            return 1;
        }

        var allSummaries = new List<ScenarioSummaryMetrics>();
        var allRawRecords = new List<RawMeasurementRecord>();
        var allHarvestResults = new List<AuditHarvestResult>();

        try
        {
            ConfigureThreadPool(config.Execution.MinWorkerThreads);
            PrintSuiteBanner(config);

            int totalRuns = config.Scenarios.Count * config.ConcurrencyLevels.Count;
            int currentRunIndex = 0;

            foreach (var scenario in config.Scenarios)
            {
                foreach (var concurrency in config.ConcurrencyLevels)
                {
                    currentRunIndex++;
                    int totalRequests = config.GetTotalRequestsFor(scenario);
                    int warmupRequests = config.GetWarmupRequestsFor(scenario);

                    await using var hostController = new ScenarioHostController(scenario, concurrency, config);
                    using var client = hostController.CreateBenchmarkClient(out var metadata, totalRequests, warmupRequests);

                    Console.Write(
                        $"  [RUN {currentRunIndex,2}/{totalRuns,2}] {scenario,-3} | VUs = {concurrency,3} | N = {totalRequests,4} ... ");
                    var swRun = Stopwatch.StartNew();

                    // Phase 1: Warm-up & Experimental State Lock
                    await hostController.ExecuteWarmupAndLockStateAsync(client, warmupRequests);

                    // Phase 2: Controlled Concurrent Measurement
                    var worker = new MeasurementWorker(
                        hostController,
                        client,
                        metadata,
                        totalRequests,
                        concurrency,
                        config.Execution.MaxErrorLogEntriesPerRun);

                    var (records, wallClockMs) = await worker.RunAllAsync();

                    // Phase 3: Statistical Aggregation
                    var summary = ResultAggregator.Aggregate(
                        metadata,
                        records,
                        wallClockMs,
                        hostController.StrategyInvocationCount,
                        hostController.ObservedRetryCount,
                        hostController.ObservedInnerTimeoutCount);

                    // Phase 4 (Week 9): In-Vivo Audit Ledger Harvest BEFORE ScenarioHostController.DisposeAsync()
                    var harvestResult = await AuditLedgerHarvester.HarvestFromLiveHostAsync(
                        hostController,
                        metadata,
                        summary,
                        records);

                    allSummaries.Add(summary);
                    allRawRecords.AddRange(records);
                    allHarvestResults.Add(harvestResult);
                    swRun.Stop();

                    Console.WriteLine(
                        $"Done ({swRun.Elapsed.TotalSeconds:F1}s) -> " +
                        $"P50={summary.ClientP50Ms,6:F2}ms | P95={summary.ClientP95Ms,6:F2}ms | P99={summary.ClientP99Ms,6:F2}ms | " +
                        $"Max={summary.ClientMaxMs,6:F2}ms | RPS={summary.ThroughputRps,7:F1} | DCR={summary.DcrPercent,6:F2}%");
                }

                Console.WriteLine();
            }

            // Compute Layer A Differential Metrics (G1 - G0 and G0 - G2) per Concurrency level
            ResultAggregator.PopulateCacheDifferentials(allSummaries);

            // Export Week 8 benchmark artifacts (JSON metadata, Summary CSV, Raw CSV, Markdown Report)
            await ArtifactExporter.ExportAllAsync(config.OutputDirectory, allSummaries, allRawRecords);

            // Week 9: Execute Controlled Governance Lifecycle Suite (G-A, G-B, G-C1, G-C2)
            Console.Write("  [GOVERNANCE SUITE] Executing G-A, G-B (Positive) & G-C1, G-C2 (Negative) Lifecycle Cases ... ");
            var swGov = Stopwatch.StartNew();
            var governanceObservations = await GovernanceLifecycleHarness.ExecuteSuiteAsync(config);
            swGov.Stop();
            Console.WriteLine($"Done ({swGov.Elapsed.TotalSeconds:F1}s) -> {governanceObservations.Count} cases captured.");

            // Week 9: 1:1 Join & Project 3 Normalized Research Datasets
            var joinedRecords = DatasetNormalizerEngine.JoinAllRuns(allHarvestResults);
            var (resilienceRows, latencyCachingRows) = DatasetNormalizerEngine.ProjectResilienceAndCachingDatasets(joinedRecords);
            var governanceRows = DatasetNormalizerEngine.ProjectGovernanceAuditDataset(governanceObservations);

            // Week 9: Verify 8 Provenance Invariants (INV-P1 .. INV-P8)
            var invariantResults = DatasetProvenanceVerifier.VerifyAll(
                allHarvestResults,
                joinedRecords,
                governanceObservations);

            var failedInvariants = invariantResults.Where(r => !r.Passed).ToList();
            if (failedInvariants.Count > 0)
            {
                string failureMessage = string.Join(
                    "; ",
                    failedInvariants.Select(f => $"{f.InvariantId} ({f.ViolationCount} violations): {f.Details}"));
                throw new InvalidOperationException($"Dataset provenance verification failed: {failureMessage}");
            }

            // Week 9: Export Normalized Datasets (CSV + JSONL), Provenance Manifest JSON & Week 9 Report
            var manifest = await DatasetArtifactExporter.ExportAllAsync(
                config.DatasetsOutputDirectory,
                allHarvestResults,
                resilienceRows,
                latencyCachingRows,
                governanceRows,
                invariantResults);

            PrintExportedArtifactsBanner(config.OutputDirectory, config.DatasetsOutputDirectory, manifest);

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("================================================================================");
            await Console.Error.WriteLineAsync("  FATAL ERROR — TADSS.BenchmarkRunner Terminated Unexpectedly");
            await Console.Error.WriteLineAsync("================================================================================");
            await Console.Error.WriteLineAsync($"  Exception Type : {ex.GetType().FullName}");
            await Console.Error.WriteLineAsync($"  Message        : {ex.Message}");
            await Console.Error.WriteLineAsync($"  StackTrace     :\n{ex.StackTrace}");
            await Console.Error.WriteLineAsync("================================================================================");
            Console.ResetColor();

            if (allSummaries.Count > 0)
            {
                try
                {
                    await Console.Error.WriteLineAsync(
                        $"  [RECOVERY] Saving {allSummaries.Count} completed run(s) to partial artifacts in '{config.OutputDirectory}'...");
                    await ArtifactExporter.ExportPartialResultsAsync(config.OutputDirectory, allSummaries, allRawRecords);
                    await Console.Error.WriteLineAsync("  [RECOVERY] Partial artifacts saved successfully.");
                }
                catch (Exception exportEx)
                {
                    await Console.Error.WriteLineAsync($"  [RECOVERY FAILED] Could not write partial results: {exportEx.Message}");
                }
            }

            return 2;
        }
    }

    private static void ConfigureThreadPool(int desiredMinThreads)
    {
        ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
        ThreadPool.SetMinThreads(
            Math.Max(minWorkers, desiredMinThreads),
            Math.Max(minIo, desiredMinThreads));
    }

    private static void PrintSuiteBanner(BenchmarkSuiteConfig config)
    {
        int timedWarmup = Math.Min(config.WarmupRequests, config.FaultTiming.TimedScenarioMaxWarmupRequests);
        Console.WriteLine("================================================================================");
        Console.WriteLine("  TADSS.BenchmarkRunner — Controlled Experimental & Dataset Normalization Suite");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"  Scenarios          : {string.Join(", ", config.Scenarios)}");
        Console.WriteLine($"  Concurrency (VUs)  : {string.Join(", ", config.ConcurrencyLevels)}");
        Console.WriteLine($"  Fast Scenario Reqs : {config.FastScenarioRequests} (+ {config.WarmupRequests} warm-up)");
        Console.WriteLine($"  Timed Fault Reqs   : {config.FaultTimedScenarioRequests} (+ {timedWarmup} warm-up)");
        Console.WriteLine($"  Resilience Budget  : {config.Resilience.ComputedAttemptAndDelayBudgetMs}ms / {config.Resilience.OuterTimeoutMs}ms SLA");
        Console.WriteLine($"  Cache Backend Mode : {config.CacheBackendMode}");
        Console.WriteLine($"  Benchmarks Dir     : {Path.GetFullPath(config.OutputDirectory)}");
        Console.WriteLine($"  Datasets Dir       : {Path.GetFullPath(config.DatasetsOutputDirectory)}");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }

    private static void PrintExportedArtifactsBanner(
        string benchmarkOutputDir,
        string datasetsOutputDir,
        DatasetProvenanceManifest manifest)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Exported Week 8 Benchmark Evidence Artifacts:");
        Console.WriteLine($"  1. {Path.Combine(benchmarkOutputDir, "benchmark_runs_metadata.json")}");
        Console.WriteLine($"  2. {Path.Combine(benchmarkOutputDir, "benchmark_summary.csv")}");
        Console.WriteLine($"  3. {Path.Combine(benchmarkOutputDir, "benchmark_raw_samples.csv")}");
        Console.WriteLine($"  4. {Path.Combine(benchmarkOutputDir, "WEEK8_BENCHMARK_REPORT.md")}");
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine($"  Exported Week 9 Normalized Research Datasets (Invariants Passed: {manifest.AllProvenanceInvariantsPassed}):");
        Console.WriteLine($"  5. {Path.Combine(datasetsOutputDir, "resilience_evaluation_dataset.csv")} (.jsonl)");
        Console.WriteLine($"  6. {Path.Combine(datasetsOutputDir, "latency_caching_dataset.csv")} (.jsonl)");
        Console.WriteLine($"  7. {Path.Combine(datasetsOutputDir, "governance_audit_dataset.csv")} (.jsonl)");
        Console.WriteLine($"  8. {Path.Combine(datasetsOutputDir, "dataset_provenance_manifest.json")}");
        Console.WriteLine($"  9. {Path.Combine(datasetsOutputDir, "WEEK9_DATASET_NORMALIZATION_REPORT.md")}");
        Console.WriteLine("================================================================================");
    }
}
