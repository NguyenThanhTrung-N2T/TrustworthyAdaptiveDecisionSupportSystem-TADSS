namespace TADSS.BenchmarkRunner.Protocol;

/// <summary>
/// Bộ phân tích và kiểm tra hợp lệ tham số dòng lệnh cho TADSS.BenchmarkRunner.
/// Hỗ trợ chọn bộ kịch bản (--suite Core7|Full19), danh sách kịch bản tùy chỉnh (--scenarios),
/// và ghi đè động các thông số Resilience/Timing từ CLI.
/// </summary>
public static class CliArgumentParser
{
    public static BenchmarkSuiteConfig ParseAndValidate(string[] args)
    {
        int fastReqs = BenchmarkDefaults.Execution.DefaultFastScenarioRequests;
        int timedReqs = BenchmarkDefaults.Execution.DefaultTimedScenarioRequests;
        int warmupReqs = BenchmarkDefaults.Execution.DefaultWarmupRequests;
        string cacheMode = BenchmarkDefaults.CacheModes.Memory;
        string outputDir = BenchmarkDefaults.DefaultOutputDirectory;
        IReadOnlyList<ExperimentScenarioId>? customScenarios = null;
        List<int>? customVus = null;

        int outerTimeoutMs = BenchmarkDefaults.Resilience.OuterTimeoutMs;
        int innerTimeoutMs = BenchmarkDefaults.Resilience.InnerTimeoutMs;
        int retryCount = BenchmarkDefaults.Resilience.RetryCount;
        int retryDelayMs = BenchmarkDefaults.Resilience.RetryDelayMs;
        int r1DelayMs = BenchmarkDefaults.FaultTiming.RecoverableFirstAttemptDelayMs;
        int r3DelayMs = BenchmarkDefaults.FaultTiming.ExhaustiveAttemptDelayMs;
        int minThreads = BenchmarkDefaults.Execution.MinWorkerThreads;

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--fast-requests":
                    fastReqs = ParsePositiveInt(args, ref i, flag);
                    break;

                case "--timed-requests":
                    timedReqs = ParsePositiveInt(args, ref i, flag);
                    break;

                case "--warmup":
                    warmupReqs = ParseNonNegativeInt(args, ref i, flag);
                    break;

                case "--cache-mode":
                    cacheMode = RequireValue(args, ref i, flag);
                    if (!cacheMode.Equals(BenchmarkDefaults.CacheModes.Memory, StringComparison.OrdinalIgnoreCase) &&
                        !cacheMode.Equals(BenchmarkDefaults.CacheModes.RedisCloud, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException(
                            $"Invalid --cache-mode '{cacheMode}'. Allowed values: '{BenchmarkDefaults.CacheModes.Memory}', '{BenchmarkDefaults.CacheModes.RedisCloud}'.");
                    }
                    break;

                case "--output-dir":
                    outputDir = RequireValue(args, ref i, flag);
                    if (string.IsNullOrWhiteSpace(outputDir))
                    {
                        throw new ArgumentException("--output-dir cannot be empty or whitespace.");
                    }
                    break;

                case "--suite":
                    string suiteName = RequireValue(args, ref i, flag);
                    customScenarios = suiteName.ToLowerInvariant() switch
                    {
                        "core7" => BenchmarkDefaults.CoreSevenScenarios,
                        "full19" or "all" => BenchmarkDefaults.DefaultScenarios,
                        _ => throw new ArgumentException($"Invalid --suite '{suiteName}'. Allowed values: 'Core7', 'Full19'.")
                    };
                    break;

                case "--scenarios":
                    customScenarios = ParseScenarios(RequireValue(args, ref i, flag));
                    break;

                case "--vus":
                    customVus = ParseConcurrencyLevels(RequireValue(args, ref i, flag));
                    break;

                case "--outer-timeout-ms":
                    outerTimeoutMs = ParsePositiveInt(args, ref i, flag);
                    break;

                case "--inner-timeout-ms":
                    innerTimeoutMs = ParsePositiveInt(args, ref i, flag);
                    break;

                case "--retry-count":
                    retryCount = ParseNonNegativeInt(args, ref i, flag);
                    break;

                case "--retry-delay-ms":
                    retryDelayMs = ParseNonNegativeInt(args, ref i, flag);
                    break;

                case "--r1-delay-ms":
                    r1DelayMs = ParsePositiveInt(args, ref i, flag);
                    break;

                case "--r3-delay-ms":
                    r3DelayMs = ParsePositiveInt(args, ref i, flag);
                    break;

                case "--min-threads":
                    minThreads = ParsePositiveInt(args, ref i, flag);
                    break;

                default:
                    throw new ArgumentException($"Unknown command-line argument '{flag}'.");
            }
        }

        return new BenchmarkSuiteConfig
        {
            Scenarios = customScenarios ?? BenchmarkDefaults.DefaultScenarios,
            ConcurrencyLevels = customVus ?? BenchmarkDefaults.DefaultConcurrencyLevels,
            FastScenarioRequests = fastReqs,
            FaultTimedScenarioRequests = timedReqs,
            WarmupRequests = warmupReqs,
            CacheBackendMode = cacheMode,
            OutputDirectory = outputDir,
            Resilience = new BenchmarkResilienceSettings
            {
                OuterTimeoutMs = outerTimeoutMs,
                InnerTimeoutMs = innerTimeoutMs,
                RetryCount = retryCount,
                RetryDelayMs = retryDelayMs
            },
            FaultTiming = new BenchmarkFaultTimingSettings
            {
                RecoverableFirstAttemptDelayMs = r1DelayMs,
                ExhaustiveAttemptDelayMs = r3DelayMs
            },
            Execution = new BenchmarkExecutionSettings
            {
                MinWorkerThreads = minThreads
            }
        };
    }

    public static string GetUsageText() =>
        """
        Usage: dotnet run --project tests/TADSS.BenchmarkRunner -- [options]
        Options:
          --suite <Core7|Full19>       Preset scenario suite (default: Full19)
          --scenarios <G0,C1,E2,...>   Comma-separated scenario IDs (G0..G2, R0..R3, C0..C3, D0..D2, E0..E4)
          --vus <1,10,50,100>          Comma-separated virtual user concurrency levels (> 0)
          --fast-requests <int>        Requests per fast scenario (> 0, default: 400)
          --timed-requests <int>       Requests per timed fault scenario (> 0, default: 200)
          --warmup <int>               Warm-up requests per run (>= 0, default: 50)
          --cache-mode <Memory|RedisCloud> Cache backend mode (default: Memory)
          --output-dir <path>          Output directory for exported benchmark artifacts
          --outer-timeout-ms <int>     Outer Polly timeout in ms (> 0, default: 500)
          --inner-timeout-ms <int>     Inner per-attempt Polly timeout in ms (> 0, default: 120)
          --retry-count <int>          Max retry attempts (>= 0, default: 2)
          --retry-delay-ms <int>       Constant retry delay in ms (>= 0, default: 30)
          --r1-delay-ms <int>          R1 first-attempt delay in ms (> 0, default: 160)
          --r3-delay-ms <int>          R2/R3 exhaustive delay in ms (> 0, default: 250)
          --min-threads <int>          Minimum ThreadPool worker threads (> 0, default: 256)
        """;

    private static string RequireValue(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for argument '{flag}'.");
        }

        return args[++index];
    }

    private static int ParsePositiveInt(string[] args, ref int index, string flag)
    {
        string raw = RequireValue(args, ref index, flag);
        if (!int.TryParse(raw, out int value) || value <= 0)
        {
            throw new ArgumentException($"Argument '{flag}' requires a positive integer (> 0), but received '{raw}'.");
        }

        return value;
    }

    private static int ParseNonNegativeInt(string[] args, ref int index, string flag)
    {
        string raw = RequireValue(args, ref index, flag);
        if (!int.TryParse(raw, out int value) || value < 0)
        {
            throw new ArgumentException($"Argument '{flag}' requires a non-negative integer (>= 0), but received '{raw}'.");
        }

        return value;
    }

    private static List<ExperimentScenarioId> ParseScenarios(string raw)
    {
        var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("--scenarios must contain at least one valid scenario ID.");
        }

        var list = new List<ExperimentScenarioId>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!Enum.TryParse<ExperimentScenarioId>(token, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                throw new ArgumentException(
                    $"Invalid scenario ID '{token}' in --scenarios. Valid values: {string.Join(", ", Enum.GetNames<ExperimentScenarioId>())}.");
            }

            list.Add(parsed);
        }

        return list;
    }

    private static List<int> ParseConcurrencyLevels(string raw)
    {
        var tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("--vus must contain at least one positive integer.");
        }

        var list = new List<int>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out int vu) || vu <= 0)
            {
                throw new ArgumentException($"Invalid concurrency level '{token}' in --vus. All VU levels must be positive integers (> 0).");
            }

            list.Add(vu);
        }

        return list;
    }
}
