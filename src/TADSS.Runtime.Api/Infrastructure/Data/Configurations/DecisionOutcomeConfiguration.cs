using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.Runtime.Api.Infrastructure.Data.Configurations;

/// <summary>
/// Cấu hình bảng <c>tadss.decision_outcomes</c> theo DECISION_PERSISTENCE_INVARIANTS B.3 (append-only).
/// </summary>
public class DecisionOutcomeConfiguration : IEntityTypeConfiguration<DecisionOutcome>
{
    public void Configure(EntityTypeBuilder<DecisionOutcome> builder)
    {
        builder.ToTable("decision_outcomes", "tadss", table =>
        {
            table.HasCheckConstraint(
                "ck_dout_outcome",
                "outcome IN ('COMPLETED', 'NO_SHOW', 'CANCELLED_BY_CUSTOMER', 'CANCELLED_BY_STAFF', 'PAYMENT_EXPIRED', 'APPROVAL_EXPIRED', 'REJECTED')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.DecisionId).HasColumnName("decision_id").IsRequired();
        builder.Property(e => e.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.ReportedAt).HasColumnName("reported_at").IsRequired();

        // Tối đa một outcome cho mỗi quyết định (INV-6)
        builder.HasIndex(e => e.DecisionId, "uq_decision_outcomes_decision_id")
            .HasDatabaseName("uq_decision_outcomes_decision_id")
            .IsUnique();

        builder.HasOne<DecisionExecution>()
            .WithOne()
            .HasForeignKey<DecisionOutcome>(e => e.DecisionId)
            .HasPrincipalKey<DecisionExecution>(e => e.DecisionId)
            .HasConstraintName("fk_decision_outcomes_decision_id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
