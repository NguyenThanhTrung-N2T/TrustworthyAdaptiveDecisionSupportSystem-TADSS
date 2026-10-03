using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TADSS.Application.Common;
using TADSS.Infrastructure.Persistence.Entities;

namespace TADSS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình bảng <c>tadss.decision_overrides</c> theo DECISION_PERSISTENCE_INVARIANTS B.2 (append-only).
/// </summary>
public class DecisionOverrideConfiguration : IEntityTypeConfiguration<DecisionOverrideEntity>
{
    public void Configure(EntityTypeBuilder<DecisionOverrideEntity> builder)
    {
        builder.ToTable("decision_overrides", "tadss", table =>
        {
            table.HasCheckConstraint("ck_do_action", "action IN ('APPROVE', 'REJECT')");
            table.HasCheckConstraint("ck_do_deposit_rate", "deposit_rate BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_do_deposit_amount", "deposit_amount >= 0");
            table.HasCheckConstraint("ck_do_reason_length", "char_length(reason) BETWEEN 5 AND 500");
            table.HasCheckConstraint("ck_do_deposit", "(action = 'APPROVE') = (deposit_rate IS NOT NULL AND deposit_amount IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.DecisionId).HasColumnName("decision_id").IsRequired();
        builder.Property(e => e.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(e => e.DepositRate).HasColumnName("deposit_rate").HasPrecision(5, 4);
        builder.Property(e => e.DepositAmount).HasColumnName("deposit_amount").HasPrecision(14, 2);
        builder.Property(e => e.ActorId).HasColumnName("actor_id").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason").HasColumnType("text").IsRequired();
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(ContractLimits.MaxIdempotencyKeyLength).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        // Tối đa một override cho mỗi quyết định (INV-5)
        builder.HasIndex(e => e.DecisionId, "uq_decision_overrides_decision_id")
            .HasDatabaseName("uq_decision_overrides_decision_id")
            .IsUnique();

        builder.HasOne<DecisionExecutionEntity>()
            .WithOne()
            .HasForeignKey<DecisionOverrideEntity>(e => e.DecisionId)
            .HasPrincipalKey<DecisionExecutionEntity>(e => e.DecisionId)
            .HasConstraintName("fk_decision_overrides_decision_id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
