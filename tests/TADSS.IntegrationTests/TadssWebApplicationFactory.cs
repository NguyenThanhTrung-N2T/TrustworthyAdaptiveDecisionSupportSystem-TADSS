using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;

namespace TADSS.IntegrationTests;

public class TadssWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TadssDbContext>));

            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // Register InMemory database for integration testing
            services.AddDbContext<TadssDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_dbName);
                options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());
            });
        });
    }
}
