using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TADSS.Application.Common;
using TADSS.Infrastructure.Persistence.Entities;

namespace TADSS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình bảng <c>tadss.decision_executions</c> theo DECISION_PERSISTENCE_INVARIANTS B.1 (cột, CHECK, unique, index).
/// </summary>
public class DecisionExecutionConfiguration : IEntityTypeConfiguration<DecisionExecutionEntity>
{
    public void Configure(EntityTypeBuilder<DecisionExecutionEntity> builder)
    {
        builder.ToTable("decision_executions", "tadss", table =>
        {
            table.HasCheckConstraint("ck_de_role", "role IN ('PRIMARY', 'SHADOW')");
            table.HasCheckConstraint("ck_de_status", "status IN ('APPROVED', 'NEED_APPROVAL', 'REJECTED')");
            table.HasCheckConstraint("ck_de_deposit_rate", "deposit_rate BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_de_deposit_amount", "deposit_amount >= 0");
            table.HasCheckConstraint("ck_de_execution_type", "execution_type IN ('AUTOMATED', 'FALLBACK', 'CACHE_HIT')");
            table.HasCheckConstraint("ck_de_execution_time_ms", "execution_time_ms >= 0");
            table.HasCheckConstraint("ck_de_deposit", "(status = 'APPROVED') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)");
            table.HasCheckConstraint("ck_de_approval", "(status = 'NEED_APPROVAL') = (approval_expires_at IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        // decision_id là định danh logic duy nhất; là khóa phụ để override/outcome tham chiếu tới
        builder.HasAlternateKey(e => e.DecisionId).HasName("uq_decision_executions_decision_id");
        builder.Property(e => e.DecisionId).HasColumnName("decision_id").IsRequired();
        builder.Property(e => e.ReservationId).HasColumnName("reservation_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(10).IsRequired();

        builder.Property(e => e.StrategyName).HasColumnName("strategy_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.StrategyVersion).HasColumnName("strategy_version").HasMaxLength(20).IsRequired();
        builder.Property(e => e.StrategyStatus).HasColumnName("strategy_status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.DepositRate).HasColumnName("deposit_rate").HasPrecision(5, 4);
        builder.Property(e => e.DepositAmount).HasColumnName("deposit_amount").HasPrecision(14, 2);
        builder.Property(e => e.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired();

        builder.Property(e => e.ExecutionType).HasColumnName("execution_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ExecutionTimeMs).HasColumnName("execution_time_ms").IsRequired();
        builder.Property(e => e.FallbackUsed).HasColumnName("fallback_used").IsRequired();
        builder.Property(e => e.FailureReason).HasColumnName("failure_reason").HasColumnType("text");
        builder.Property(e => e.CacheHit).HasColumnName("cache_hit").IsRequired();
        builder.Property(e => e.ApprovalExpiresAt).HasColumnName("approval_expires_at");

        builder.Property(e => e.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(e => e.RequestPayload).HasColumnName("request_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ResponsePayload).HasColumnName("response_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(ContractLimits.MaxIdempotencyKeyLength);
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(ContractLimits.MaxCorrelationIdLength);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        // Idempotency chỉ ràng buộc bản PRIMARY; bản SHADOW được phép trùng khóa (INV-2, INV-8)
        builder.HasIndex(e => e.IdempotencyKey, "uq_decision_idempotency_key")
            .HasDatabaseName("uq_decision_idempotency_key")
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL AND role = 'PRIMARY'");

        builder.HasIndex(e => new { e.ReservationId, e.CreatedAt }, "idx_de_reservation_created")
            .HasDatabaseName("idx_de_reservation_created")
            .IsDescending(false, true);

        // Hàng chờ duyệt theo chi nhánh (B.5)
        builder.HasIndex(e => new { e.BranchId, e.CreatedAt }, "idx_de_pending")
            .HasDatabaseName("idx_de_pending")
            .HasFilter("status = 'NEED_APPROVAL' AND role = 'PRIMARY'");

        builder.HasIndex(e => e.RequestHash, "idx_de_request_hash").HasDatabaseName("idx_de_request_hash");
        builder.HasIndex(e => e.CreatedAt, "idx_de_created_at").HasDatabaseName("idx_de_created_at");
    }
}
