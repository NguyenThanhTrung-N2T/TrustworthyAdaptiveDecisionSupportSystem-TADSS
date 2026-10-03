using Microsoft.EntityFrameworkCore;
using TADSS.Infrastructure.Persistence.Entities;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// DbContext của schema <c>tadss</c>: ba bảng audit append-only của quyết định v2
/// (<c>decision_executions</c>, <c>decision_overrides</c>, <c>decision_outcomes</c>).
/// </summary>
public class TadssDbContext : DbContext
{
    public TadssDbContext(DbContextOptions<TadssDbContext> options) : base(options)
    {
    }

    public DbSet<DecisionExecution> DecisionExecutions => Set<DecisionExecution>();

    public DbSet<DecisionOverride> DecisionOverrides => Set<DecisionOverride>();

    public DbSet<DecisionOutcome> DecisionOutcomes => Set<DecisionOutcome>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("tadss");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TadssDbContext).Assembly);
    }
}
