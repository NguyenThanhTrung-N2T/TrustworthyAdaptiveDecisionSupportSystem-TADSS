using Microsoft.EntityFrameworkCore;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.Runtime.Api.Infrastructure.Data;

public class TadssDbContext : DbContext
{
    public TadssDbContext(DbContextOptions<TadssDbContext> options) : base(options)
    {
    }

    public DbSet<DecisionExecution> DecisionExecutions => Set<DecisionExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.HasDefaultSchema("tadss");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TadssDbContext).Assembly);
    }
}
