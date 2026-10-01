using System.Diagnostics;
using TADSS.BenchmarkRunner.Export;
using TADSS.BenchmarkRunner.Harness;
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

                    allSummaries.Add(summary);
                    allRawRecords.AddRange(records);
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

            PrintExportedArtifactsBanner(config.OutputDirectory);

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
        Console.WriteLine("  TADSS.BenchmarkRunner — Micro-benchmark phòng lab (EF InMemory, không dùng làm minh chứng SLA)");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"  Scenarios          : {string.Join(", ", config.Scenarios)}");
        Console.WriteLine($"  Concurrency (VUs)  : {string.Join(", ", config.ConcurrencyLevels)}");
        Console.WriteLine($"  Fast Scenario Reqs : {config.FastScenarioRequests} (+ {config.WarmupRequests} warm-up)");
        Console.WriteLine($"  Timed Fault Reqs   : {config.FaultTimedScenarioRequests} (+ {timedWarmup} warm-up)");
        Console.WriteLine($"  Resilience Budget  : {config.Resilience.ComputedAttemptAndDelayBudgetMs}ms / {config.Resilience.OuterTimeoutMs}ms SLA");
        Console.WriteLine($"  Cache Backend Mode : {config.CacheBackendMode}");
        Console.WriteLine($"  Benchmarks Dir     : {Path.GetFullPath(config.OutputDirectory)}");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }

    private static void PrintExportedArtifactsBanner(string benchmarkOutputDir)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine("  Exported Micro-benchmark Artifacts:");
        Console.WriteLine($"  1. {Path.Combine(benchmarkOutputDir, "benchmark_runs_metadata.json")}");
        Console.WriteLine($"  2. {Path.Combine(benchmarkOutputDir, "benchmark_summary.csv")}");
        Console.WriteLine($"  3. {Path.Combine(benchmarkOutputDir, "benchmark_raw_samples.csv")}");
        Console.WriteLine($"  4. {Path.Combine(benchmarkOutputDir, "WEEK8_BENCHMARK_REPORT.md")}");
        Console.WriteLine("================================================================================");
    }
}
