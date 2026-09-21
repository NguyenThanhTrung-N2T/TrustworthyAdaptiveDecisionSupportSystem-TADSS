using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Serilog
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Đăng ký Services
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApiServices();

var app = builder.Build();

// Eager startup validation for StrategyResolver (fail-fast on startup if strategy config is invalid)
app.Services.GetRequiredService<IStrategyResolver>();

// 1. Correlation ID Middleware (Canonical Correlation Propagation)
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Pipeline xử lý lỗi RFC 7807 ProblemDetails
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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

