using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.Runtime.Api.Infrastructure.Data.Configurations;

public class DecisionExecutionConfiguration : IEntityTypeConfiguration<DecisionExecution>
{
    public void Configure(EntityTypeBuilder<DecisionExecution> builder)
    {
        builder.ToTable("decision_executions", "tadss");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        // Columns mapping
        builder.Property(e => e.DecisionId).HasColumnName("decision_id").IsRequired();
        builder.Property(e => e.ReservationId).HasColumnName("reservation_id").IsRequired();
        builder.Property(e => e.StrategyName).HasColumnName("strategy_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.StrategyVersion).HasColumnName("strategy_version").HasMaxLength(20).HasDefaultValue("1.0.0").IsRequired();
        
        builder.Property(e => e.ExecutionType).HasColumnName("execution_type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.ExecutionTimeMs).HasColumnName("execution_time_ms").IsRequired();
        builder.Property(e => e.FallbackUsed).HasColumnName("fallback_used").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.FailureReason).HasColumnName("failure_reason");
        builder.Property(e => e.CacheHit).HasColumnName("cache_hit").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.NeedApproval).HasColumnName("need_approval").HasDefaultValue(false).IsRequired();

        builder.Property(e => e.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.ResponsePayload).HasColumnName("response_payload").HasColumnType("jsonb").IsRequired();

        // Override columns
        builder.Property(e => e.IsOverridden).HasColumnName("is_overridden").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.OverriddenBy).HasColumnName("overridden_by").HasMaxLength(100);
        builder.Property(e => e.OverrideReason).HasColumnName("override_reason");

        // Concurrency & Timestamps (Quản lý thuần túy bởi Interceptor)
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        builder.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken().IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // 3 Indexes cơ bản theo spec
        builder.HasIndex(e => e.RequestHash).HasDatabaseName("idx_decision_executions_request_hash");
        builder.HasIndex(e => e.ReservationId).HasDatabaseName("idx_decision_executions_reservation_id");
        builder.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_decision_executions_created_at");

        // Unique Constraint: Idempotency (decision_id, idempotency_key)
        builder.HasIndex(e => new { e.DecisionId, e.IdempotencyKey })
            .HasDatabaseName("uq_decision_idempotency")
            .IsUnique();

        // Partial Index: Tối ưu riêng cho Manager Dashboard (Pending Approvals)
        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("idx_decision_executions_pending_approval")
            .HasFilter("need_approval = true AND is_overridden = false");
    }
}
