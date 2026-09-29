using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TADSS.BenchmarkRunner.Harness;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.BenchmarkRunner.Normalization;

/// <summary>
/// Thu hoạch trực tiếp sổ cái kiểm toán (In-Vivo Audit Ledger Harvester) từ TadssDbContext
/// khi ScenarioHostController vẫn còn hoạt động (trước DisposeAsync),
/// và phân tách chính xác tập bản ghi pha Measurement khỏi tập bản ghi Warm-up / Circuit-Trip.
/// </summary>
public static class AuditLedgerHarvester
{
    public static async Task<AuditHarvestResult> HarvestFromLiveHostAsync(
        ScenarioHostController hostController,
        RunMetadata metadata,
        ScenarioSummaryMetrics summary,
        IReadOnlyList<RawMeasurementRecord> measurementRecords,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostController);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(measurementRecords);

        using var scope = hostController.CreateServiceScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TadssDbContext>();

        var allDbExecutions = await dbContext.DecisionExecutions
            .AsNoTracking()
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return PartitionByMeasurementDecisionIds(metadata, summary, measurementRecords, allDbExecutions);
    }

    /// <summary>
    /// Phân tách tập bản ghi kiểm toán dựa trên tập khóa MeasurementDecisionIds (K_meas)
    /// theo đúng Hợp đồng Kết nối Mục 3.2 trong DATASET_SCHEMA_SPECIFICATION_V1.md.
    /// </summary>
    public static AuditHarvestResult PartitionByMeasurementDecisionIds(
        RunMetadata metadata,
        ScenarioSummaryMetrics summary,
        IReadOnlyList<RawMeasurementRecord> measurementRecords,
        IReadOnlyList<DecisionExecution> allDbExecutions)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(measurementRecords);
        ArgumentNullException.ThrowIfNull(allDbExecutions);

        var measurementDecisionIds = new HashSet<Guid>();
        foreach (var record in measurementRecords)
        {
            if (record.DecisionId != Guid.Empty)
            {
                measurementDecisionIds.Add(record.DecisionId);
            }
        }

        var selected = new List<DecisionExecution>(measurementDecisionIds.Count);
        var excluded = new List<DecisionExecution>(Math.Max(0, allDbExecutions.Count - measurementDecisionIds.Count));

        foreach (var execution in allDbExecutions)
        {
            if (measurementDecisionIds.Contains(execution.DecisionId))
            {
                selected.Add(CloneExecution(execution));
            }
            else
            {
                excluded.Add(CloneExecution(execution));
            }
        }

        return new AuditHarvestResult
        {
            Metadata = metadata,
            Summary = summary,
            MeasurementRecords = measurementRecords,
            SelectedAuditExecutions = selected,
            ExcludedNonMeasurementExecutions = excluded,
            TotalDbExecutionsCount = allDbExecutions.Count
        };
    }

    public static DecisionExecution CloneExecution(DecisionExecution source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DecisionExecution
        {
            Id = source.Id,
            DecisionId = source.DecisionId,
            ReservationId = source.ReservationId,
            StrategyName = source.StrategyName,
            StrategyVersion = source.StrategyVersion,
            ExecutionType = source.ExecutionType,
            ExecutionTimeMs = source.ExecutionTimeMs,
            FallbackUsed = source.FallbackUsed,
            FailureReason = source.FailureReason,
            CacheHit = source.CacheHit,
            NeedApproval = source.NeedApproval,
            RequestHash = source.RequestHash,
            ResponsePayload = source.ResponsePayload,
            IdempotencyKey = source.IdempotencyKey,
            IsOverridden = source.IsOverridden,
            OverriddenBy = source.OverriddenBy,
            OverrideReason = source.OverrideReason,
            Version = source.Version,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }
}
