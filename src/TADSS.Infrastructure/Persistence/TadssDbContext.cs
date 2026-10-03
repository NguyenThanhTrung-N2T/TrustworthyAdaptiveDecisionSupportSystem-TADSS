using Microsoft.EntityFrameworkCore;
using TADSS.Infrastructure.Persistence.Entities;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// DbContext của schema <c>tadss</c>: ba bảng audit append-only của quyết định
/// (<c>decision_executions</c>, <c>decision_overrides</c>, <c>decision_outcomes</c>).
/// </summary>
public class TadssDbContext : DbContext
{
    public TadssDbContext(DbContextOptions<TadssDbContext> options) : base(options)
    {
    }

    public DbSet<DecisionExecutionEntity> DecisionExecutions => Set<DecisionExecutionEntity>();

    public DbSet<DecisionOverrideEntity> DecisionOverrides => Set<DecisionOverrideEntity>();

    public DbSet<DecisionOutcomeEntity> DecisionOutcomes => Set<DecisionOutcomeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("tadss");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TadssDbContext).Assembly);
    }
}
