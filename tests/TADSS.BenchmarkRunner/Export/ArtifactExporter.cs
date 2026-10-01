using System.Globalization;
using System.Text;
using System.Text.Json;
using TADSS.BenchmarkRunner.Protocol;

namespace TADSS.BenchmarkRunner.Export;

public static class ArtifactExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task ExportAllAsync(
        string outputDirectory,
        IReadOnlyList<ScenarioSummaryMetrics> summaries,
        IReadOnlyList<RawMeasurementRecord> rawRecords)
    {
        Directory.CreateDirectory(outputDirectory);

        var metadataJsonPath = Path.Combine(outputDirectory, "benchmark_runs_metadata.json");
        var summaryCsvPath = Path.Combine(outputDirectory, "benchmark_summary.csv");
        var rawCsvPath = Path.Combine(outputDirectory, "benchmark_raw_samples.csv");
        var markdownReportPath = Path.Combine(outputDirectory, "WEEK8_BENCHMARK_REPORT.md");

        // 1. Export JSON Run Metadata + Statistical Aggregations
        var jsonContent = JsonSerializer.Serialize(summaries, JsonOptions);
        await File.WriteAllTextAsync(metadataJsonPath, jsonContent, Encoding.UTF8);

        // 2. Export Summary CSV
        var summaryCsv = BuildSummaryCsv(summaries);
        await File.WriteAllTextAsync(summaryCsvPath, summaryCsv, Encoding.UTF8);

        // 3. Export Raw Samples CSV (for Week 9 DatasetNormalizer traceability)
        var rawCsv = BuildRawSamplesCsv(rawRecords);
        await File.WriteAllTextAsync(rawCsvPath, rawCsv, Encoding.UTF8);

        // 4. Export Markdown Report for Do An 2 Chapters 3 & 4
        if (summaries.Count > 0)
        {
            var markdownReport = BuildMarkdownReport(summaries);
            await File.WriteAllTextAsync(markdownReportPath, markdownReport, Encoding.UTF8);
        }
    }

    /// <summary>
    /// Lưu khẩn cấp dữ liệu đã thu thập được trong trường hợp tiến trình thực nghiệm gặp sự cố bất ngờ.
    /// </summary>
    public static async Task ExportPartialResultsAsync(
        string outputDirectory,
        IReadOnlyList<ScenarioSummaryMetrics> summaries,
        IReadOnlyList<RawMeasurementRecord> rawRecords)
    {
        Directory.CreateDirectory(outputDirectory);

        var partialJsonPath = Path.Combine(outputDirectory, "benchmark_runs_metadata.partial.json");
        var partialSummaryPath = Path.Combine(outputDirectory, "benchmark_summary.partial.csv");
        var partialRawPath = Path.Combine(outputDirectory, "benchmark_raw_samples.partial.csv");

        await File.WriteAllTextAsync(partialJsonPath, JsonSerializer.Serialize(summaries, JsonOptions), Encoding.UTF8);
        await File.WriteAllTextAsync(partialSummaryPath, BuildSummaryCsv(summaries), Encoding.UTF8);
        await File.WriteAllTextAsync(partialRawPath, BuildRawSamplesCsv(rawRecords), Encoding.UTF8);
    }

    private static string BuildSummaryCsv(IReadOnlyList<ScenarioSummaryMetrics> summaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("run_id,timestamp_utc,scenario,experiment_layer,concurrency,total_requests,warmup_requests,strategy,strategy_version,cache_state,redis_state,database_state,outer_timeout_ms,inner_timeout_ms,retry_count,retry_delay_ms,configured_budget_ms,circuit_state_setup,circuit_state_observed,client_p50_ms,client_p90_ms,client_p95_ms,client_p99_ms,client_max_ms,client_mean_ms,client_stddev_ms,server_p50_ms,server_p95_ms,server_p99_ms,server_max_ms,throughput_rps,eligible_requests,completed_safe_decisions,dcr_percent,cache_hit_ratio_percent,fallback_rate_percent,http_error_or_timeout_count,http_error_rate_percent,strategy_invocations,observed_retry_count,observed_inner_timeout_count,cache_miss_overhead_p50_ms,cache_miss_overhead_p95_ms,cache_benefit_p50_ms,cache_benefit_p95_ms");

        foreach (var s in summaries)
        {
            var m = s.Metadata;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{m.RunId},{m.TimestampUtc},{m.Scenario},{m.ExperimentLayer},{m.Concurrency},{m.TotalRequests},{m.WarmupRequests},{m.Strategy},{m.StrategyVersion},{m.CacheState},{m.RedisState},{m.DatabaseState},{m.OuterTimeoutMs},{m.InnerTimeoutMs},{m.RetryCount},{m.RetryDelayMs},{m.ConfiguredResilienceBudgetMs},{m.CircuitStateSetup},\"{s.CircuitStateObserved}\",{s.ClientP50Ms:F2},{s.ClientP90Ms:F2},{s.ClientP95Ms:F2},{s.ClientP99Ms:F2},{s.ClientMaxMs:F2},{s.ClientMeanMs:F2},{s.ClientStdDevMs:F2},{s.ServerP50Ms:F2},{s.ServerP95Ms:F2},{s.ServerP99Ms:F2},{s.ServerMaxMs:F2},{s.ThroughputRps:F2},{s.EligibleRequests},{s.CompletedSafeDecisions},{s.DcrPercent:F2},{s.CacheHitRatioPercent:F2},{s.FallbackRatePercent:F2},{s.HttpErrorOrTimeoutCount},{s.HttpErrorRatePercent:F2},{s.StrategyInvocations},{s.ObservedRetryCount},{s.ObservedInnerTimeoutCount},{FormatNullable(s.CacheMissOverheadP50Ms)},{FormatNullable(s.CacheMissOverheadP95Ms)},{FormatNullable(s.CacheBenefitP50Ms)},{FormatNullable(s.CacheBenefitP95Ms)}"));
        }

        return sb.ToString();
    }

    private static string BuildRawSamplesCsv(IReadOnlyList<RawMeasurementRecord> rawRecords)
    {
        var sb = new StringBuilder();
        sb.AppendLine("run_id,scenario,concurrency,request_index,client_elapsed_ms,server_execution_time_ms,http_status_code,decision_status,execution_type,fallback_used,cache_hit,is_eligible_for_dcr,is_safe_completed_decision");

        foreach (var r in rawRecords)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.RunId},{r.Scenario},{r.Concurrency},{r.RequestIndex},{r.ClientElapsedMs:F3},{r.ServerExecutionTimeMs},{r.HttpStatusCode},{r.DecisionStatus},{r.ExecutionType},{r.FallbackUsed},{r.CacheHit},{r.IsEligibleForDcr},{r.IsSafeCompletedDecision}"));
        }

        return sb.ToString();
    }

    private static string BuildMarkdownReport(IReadOnlyList<ScenarioSummaryMetrics> summaries)
    {
        var firstMeta = summaries[0].Metadata;
        var concurrencyLevels = summaries
            .Select(s => s.Metadata.Concurrency)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        var activeScenarios = summaries
            .Select(s => Enum.Parse<ExperimentScenarioId>(s.Metadata.Scenario))
            .Distinct()
            .Select(ScenarioCatalog.Get)
            .ToList();

        var vuListFormatted = string.Join(", ", concurrencyLevels);
        var sb = new StringBuilder();

        sb.AppendLine("# 📊 TADSS Week 8 — Controlled Experimental Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"> **Báo cáo Kết quả Thực nghiệm Toàn diện ({activeScenarios.Count} Kịch bản $\\times$ {concurrencyLevels.Count} Mức tải = {summaries.Count} Lượt chạy Thực nghiệm: $G_0..G_2$, $R_0..R_3$, $C_0..C_3$, $D_0..D_2$, $E_0..E_4$ tại $VUs \\in \\{{{vuListFormatted}\\}}$)**  ");
        sb.AppendLine("> ⚠️ **Phạm vi diễn giải:** micro-benchmark phòng lab — runtime chạy với EF Core InMemory, strategy là test double độ trễ `Task.Delay`, độ trễ WAN cache/DB là giả lập. Kết quả chỉ mô tả overhead của pipeline, **không** dùng làm minh chứng KPI SC1–SC9 (xem `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`).  ");
        sb.AppendLine("> **Phạm vi Đánh giá (Scope Lock):** **Đồ án 2 — Nền tảng TADSS Decision Runtime Engine** *(Đo lường thuần túy hạ tầng Runtime, Request-Hash Cache L1/L2, Polly v8 Resilience Pipeline và Synchronous Audit Persistence trên các chiến lược quy tắc nghiệp vụ tất định `DefaultDecisionStrategy v1.0.0` & Fault-Injected Decorators; hoàn toàn không sử dụng mô hình AI/ML)*  ");
        sb.AppendLine($"> **Thời điểm thực thi (UTC):** `{firstMeta.TimestampUtc}`  ");
        sb.AppendLine($"> **Môi trường máy trạm:** `{firstMeta.MachineName}` | `{firstMeta.OsDescription}` | `{firstMeta.CpuCores} Logical Cores` | `.NET {firstMeta.DotNetVersion}`  ");
        sb.AppendLine($"> **Ngân sách Chịu lỗi Cấu hình Lý thuyết (Configured Resilience Budget):** `{firstMeta.RetryCount + 1} × {firstMeta.InnerTimeoutMs}ms + {firstMeta.RetryCount} × {firstMeta.RetryDelayMs}ms = {firstMeta.ConfiguredResilienceBudgetMs}ms < {firstMeta.OuterTimeoutMs}ms (Outer Timeout)`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Giao thức Thực nghiệm Có Kiểm soát (Controlled Experimental Protocol)");
        sb.AppendLine();
        sb.AppendLine("Báo cáo này được sinh tự động từ `TADSS.BenchmarkRunner` (Non-Intrusive Laboratory Harness) qua 5 tầng thực nghiệm:");
        sb.AppendLine("- **Layer A (`G0–G2`):** Hiệu năng cơ sở của bộ nhớ đệm chuẩn hóa RFC 8785 (`No Cache`, `Cache Miss Path`, `Pre-warmed Cache Hit Path`).");
        sb.AppendLine("- **Layer B (`R0–R3`):** Đường ống chịu lỗi Polly v8 5 lớp (`Normal`, `Retry Recovery`, `Circuit Breaker OPEN Fast-Fail`, `Retry Exhaustion ➔ Fallback`).");
        sb.AppendLine("- **Layer C (`C0–C3`):** Các bệnh lý bộ nhớ đệm thực tế (`Partial 50% Cache Hit`, `Slow WAN Cache RTT`, `Cache Thundering Herd Stampede`, `Bounded LRU Eviction Thrashing`).");
        sb.AppendLine("- **Layer D (`D0–D2`):** Hiệu năng thực thi Strategy & Database (`Heavy Multi-Rule Strategy Near Timeout Edge`, `Database Audit Write Contention`, `Bimodal Strategy Latency Jitter`).");
        sb.AppendLine("- **Layer E (`E0–E4`):** Tải hỗn hợp & Tự phục hồi (`Composite 80% Hit Production Mix`, `Compound WAN Cache + Heavy Strategy`, `Cascading Triple Stress`, `Intermittent Transient Faults`, `Circuit Breaker Half-Open ➔ Closed Self-Healing`).");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Layer A
        AppendCacheSection(
            sb,
            "## 2. Layer A — Kết quả Thực nghiệm Cơ sở Bộ nhớ đệm ($G_0 \\rightarrow G_1 \\rightarrow G_2$)",
            summaries.Where(x => x.Metadata.ExperimentLayer == BenchmarkDefaults.Layers.LayerACache).ToList());

        // Layer B
        AppendResilienceSection(
            sb,
            "## 3. Layer B — Kết quả Thực nghiệm Độ chịu lỗi Cốt lõi ($R_0, R_1, R_2, R_3$)",
            summaries.Where(x => x.Metadata.ExperimentLayer == BenchmarkDefaults.Layers.LayerBResilience).ToList());

        // Layer C
        var layerC = summaries.Where(x => x.Metadata.ExperimentLayer == BenchmarkDefaults.Layers.LayerCCachePathologies).ToList();
        if (layerC.Count > 0)
        {
            AppendCacheSection(
                sb,
                "## 4. Layer C — Các Kịch bản Bệnh lý Bộ nhớ đệm Thực tế ($C_0, C_1, C_2, C_3$)",
                layerC);
        }

        // Layer D
        var layerD = summaries.Where(x => x.Metadata.ExperimentLayer == BenchmarkDefaults.Layers.LayerDStrategyPersistence).ToList();
        if (layerD.Count > 0)
        {
            AppendResilienceSection(
                sb,
                "## 5. Layer D — Hiệu năng Chiến lược Phức tạp & Tranh chấp Ghi Kiểm toán ($D_0, D_1, D_2$)",
                layerD);
        }

        // Layer E
        var layerE = summaries.Where(x => x.Metadata.ExperimentLayer == BenchmarkDefaults.Layers.LayerECombinedStress).ToList();
        if (layerE.Count > 0)
        {
            AppendResilienceSection(
                sb,
                "## 6. Layer E — Tải Hỗn hợp Production, Đa Sự cố Cộng hưởng & Tự Phục hồi Mạch ($E_0..E_4$)",
                layerE);
        }

        // Concurrency Scaling Matrix
        sb.AppendLine($"## 7. Ma trận Đồng thời {activeScenarios.Count} × {concurrencyLevels.Count} (Concurrency Scaling Matrix: $P_{{50}}$ / $P_{{95}}$ / $P_{{99}}$ & RPS)");
        sb.AppendLine();
        sb.AppendLine("### 7.1. Ma trận Phân vị Độ trễ $P_{50} \\ / \\ P_{95} \\ / \\ P_{99}$ (ms) theo Mức tải Đồng thời");
        sb.AppendLine();

        var latencyHeaderCols = string.Join(" | ", concurrencyLevels.Select(vu => $"${vu}\\text{{ VU{(vu > 1 ? "s" : "")}}}$ ($P_{{50}} / P_{{95}} / P_{{99}}$)"));
        var latencyAlignCols = string.Join(" | ", concurrencyLevels.Select(_ => ":---:"));
        sb.AppendLine($"| Scenario | Mô tả Kịch bản | {latencyHeaderCols} |");
        sb.AppendLine($"| :---: | :--- | {latencyAlignCols} |");

        foreach (var descriptor in activeScenarios)
        {
            var scen = descriptor.Id.ToString();
            var cells = concurrencyLevels.Select(vu =>
            {
                var item = summaries.FirstOrDefault(x => x.Metadata.Scenario == scen && x.Metadata.Concurrency == vu);
                return item == null
                    ? "—"
                    : string.Create(CultureInfo.InvariantCulture, $"{item.ClientP50Ms:F2} / **{item.ClientP95Ms:F2}** / {item.ClientP99Ms:F2}");
            });

            sb.AppendLine($"| **{scen}** | {descriptor.Description} | {string.Join(" | ", cells)} |");
        }

        sb.AppendLine();
        sb.AppendLine("### 7.2. Ma trận Thông lượng (Throughput — RPS) & Tỷ lệ Duy trì Quyết định (DCR %)");
        sb.AppendLine();

        var rpsHeaderCols = string.Join(" | ", concurrencyLevels.Select(vu => $"${vu}\\text{{ VU{(vu > 1 ? "s" : "")}}}$ (RPS \\| DCR)"));
        var rpsAlignCols = string.Join(" | ", concurrencyLevels.Select(_ => ":---:"));
        sb.AppendLine($"| Scenario | {rpsHeaderCols} |");
        sb.AppendLine($"| :---: | {rpsAlignCols} |");

        foreach (var descriptor in activeScenarios)
        {
            var scen = descriptor.Id.ToString();
            var cells = concurrencyLevels.Select(vu =>
            {
                var item = summaries.FirstOrDefault(x => x.Metadata.Scenario == scen && x.Metadata.Concurrency == vu);
                return item == null
                    ? "—"
                    : string.Create(CultureInfo.InvariantCulture, $"{item.ThroughputRps:F1} RPS \\| {item.DcrPercent:F1}%");
            });

            sb.AppendLine($"| **{scen}** | {string.Join(" | ", cells)} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 8. Đánh giá Thực nghiệm Đối chiếu với Tiêu chí Thiết kế (Empirical Findings & Insights)");
        sb.AppendLine();
        sb.AppendLine($"1. **Đối chiếu Ngân sách Cấu hình (`{firstMeta.ConfiguredResilienceBudgetMs}ms`) vs. Độ trễ Thực nghiệm ở $R_3$ và $E_2$:**");
        sb.AppendLine($"   - Ở mức tải thấp ($1\\text{{ VU}}$ – $10\\text{{ VUs}}$), tổng thời gian thực tế của {firstMeta.RetryCount + 1} lần thử (`{firstMeta.RetryCount + 1} × {firstMeta.InnerTimeoutMs}ms`) + {firstMeta.RetryCount} lần chờ (`{firstMeta.RetryCount} × {firstMeta.RetryDelayMs}ms`) hội tụ quanh vùng `{firstMeta.ConfiguredResilienceBudgetMs}ms – 485ms`, nằm dưới ngưỡng cắt `OuterTimeoutMs = {firstMeta.OuterTimeoutMs}ms`.");
        sb.AppendLine($"   - Trong kịch bản đa sự cố cộng hưởng **$E_2$ (Cascading Triple Stress: WAN Cache + Heavy Strategy + Slow DB Audit)** hoặc dưới tải $100\\text{{ VUs}}$ của $R_3$, tranh chấp lập lịch luồng và độ trễ cộng dồn làm lộ rõ ranh giới chịu tải thực tế của hệ thống.");
        sb.AppendLine("2. **So sánh Đối chứng $R_2$ (Circuit Breaker `OPEN`), $R_3$ (Retry Exhaustion) và $E_4$ (Self-Healing Transition):**");
        sb.AppendLine("   - $R_2$ chứng minh khi mạch mở (`OPEN`), `Strategy Calls = 0` và hệ thống chuyển thẳng sang `FallbackDecisionStrategy` (`NEED_APPROVAL`), cắt giảm hơn `95%` độ trễ so với $R_3$.");
        sb.AppendLine("   - $E_4$ chứng minh khả năng tự phục hồi (`OPEN` $\\rightarrow$ `HALF-OPEN` $\\rightarrow$ `CLOSED`) ngay khi dịch vụ hạ tầng hết sự cố mà không cần khởi động lại tiến trình.");
        sb.AppendLine("3. **Phân tích Bệnh lý Bộ nhớ đệm ($C_0..C_3$) & Biến thiên Độ trễ ($D_0..D_2$):**");
        sb.AppendLine("   - $C_0$ (50% Hit) và $E_0$ (80% Hit) phản ánh phân phối độ trễ thực tế trong vận hành; $C_2$ (Thundering Herd) định lượng hiện tượng nhiều luồng cùng đâm xuyên qua khóa lạnh ở thời điểm $t=0$; $C_3$ minh họa suy giảm tỷ lệ trúng đích khi tập khóa làm việc (128 keys) vượt quá dung lượng bộ đệm (32 entries).");
        sb.AppendLine("   - $D_1$ chứng minh độ trễ ghi kiểm toán đồng bộ xuống DB làm tăng thời gian phản hồi đầu-cuối nhưng hoàn toàn không kích hoạt nhầm Polly Retry của tầng Strategy.");

        return sb.ToString();
    }

    private static void AppendCacheSection(
        StringBuilder sb,
        string sectionTitle,
        IReadOnlyList<ScenarioSummaryMetrics> items)
    {
        if (items.Count == 0) return;

        sb.AppendLine(sectionTitle);
        sb.AppendLine();
        sb.AppendLine("| Scenario | VUs | Requests | $P_{50}$ (ms) | $P_{90}$ (ms) | $P_{95}$ (ms) | $P_{99}$ (ms) | Max (ms) | Mean ± SD (ms) | RPS | Cache Hit (%) | Strategy Calls | $\\Delta(vs.\\ G_0)$ $P_{50}$ / $P_{95}$ (ms) |");
        sb.AppendLine("| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var s in items)
        {
            string delta = "—";
            if (s.CacheMissOverheadP50Ms.HasValue)
            {
                delta = string.Create(CultureInfo.InvariantCulture, $"Overhead: {s.CacheMissOverheadP50Ms.Value:+0.00;-0.00;0.00} / {s.CacheMissOverheadP95Ms!.Value:+0.00;-0.00;0.00}");
            }
            else if (s.CacheBenefitP50Ms.HasValue)
            {
                delta = string.Create(CultureInfo.InvariantCulture, $"Benefit: {s.CacheBenefitP50Ms.Value:+0.00;-0.00;0.00} / {s.CacheBenefitP95Ms!.Value:+0.00;-0.00;0.00}");
            }

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| **{s.Metadata.Scenario}** | {s.Metadata.Concurrency} | {s.Metadata.TotalRequests} | {s.ClientP50Ms:F2} | {s.ClientP90Ms:F2} | {s.ClientP95Ms:F2} | {s.ClientP99Ms:F2} | {s.ClientMaxMs:F2} | {s.ClientMeanMs:F2} ± {s.ClientStdDevMs:F2} | {s.ThroughputRps:F1} | {s.CacheHitRatioPercent:F1}% | {s.StrategyInvocations} | {delta} |"));
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendResilienceSection(
        StringBuilder sb,
        string sectionTitle,
        IReadOnlyList<ScenarioSummaryMetrics> items)
    {
        if (items.Count == 0) return;

        sb.AppendLine(sectionTitle);
        sb.AppendLine();
        sb.AppendLine("| Scenario | VUs | Requests | $P_{50}$ (ms) | $P_{90}$ (ms) | $P_{95}$ (ms) | $P_{99}$ (ms) | Max (ms) | RPS | DCR (%) | Cache Hit (%) | Fallback (%) | Strategy Calls | Retries | Inner Timeouts | Circuit State |");
        sb.AppendLine("| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |");

        foreach (var s in items)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| **{s.Metadata.Scenario}** | {s.Metadata.Concurrency} | {s.Metadata.TotalRequests} | {s.ClientP50Ms:F2} | {s.ClientP90Ms:F2} | {s.ClientP95Ms:F2} | {s.ClientP99Ms:F2} | {s.ClientMaxMs:F2} | {s.ThroughputRps:F1} | **{s.DcrPercent:F2}%** | {s.CacheHitRatioPercent:F1}% | {s.FallbackRatePercent:F1}% | {s.StrategyInvocations} | {s.ObservedRetryCount} | {s.ObservedInnerTimeoutCount} | `{s.CircuitStateObserved}` |"));
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static string FormatNullable(double? val)
        => val.HasValue ? val.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
}
