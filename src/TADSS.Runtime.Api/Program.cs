using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using TADSS.Application.Strategies;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Serilog
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Đăng ký Services
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApiServices();

var app = builder.Build();

// Eager startup validation cho registry strategy: tham số strategy/ActiveStrategyName sai thì không khởi động
app.Services.GetRequiredService<IStrategyRegistry>();

// Tự áp migration khi khởi động — chỉ bật ở E-LOCAL (docker compose); Neon/staging giữ mặc định false
if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup)
{
    app.Logger.LogWarning(
        "Tadss:Database:ApplyMigrationsOnStartup đang bật: tự áp migration khi khởi động chỉ dùng cho E-LOCAL, không bật cho Neon/staging.");
    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<TadssDbContext>().Database.MigrateAsync();
}

// 1. Correlation ID Middleware (Canonical Correlation Propagation)
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Pipeline xử lý lỗi RFC 7807 ProblemDetails
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v2/swagger.json", "TADSS Decision Runtime API v2"));
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

// Health Check Endpoints chuẩn Cloud-Native
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health");

app.Run();

public partial class Program { }

