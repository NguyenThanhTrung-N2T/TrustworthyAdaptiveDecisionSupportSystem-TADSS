using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TADSS.Application;
using TADSS.Infrastructure;
using TADSS.Runtime.Api.Composition;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Serilog
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Đăng ký Services: mỗi tầng tự đăng ký phần của mình
builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddStrategies(builder.Configuration)
    .AddPresentation(builder.Configuration);

var app = builder.Build();

await StartupTasks.RunAsync(app);

app.UseTadssRequestPipeline();

app.MapControllers();

// Health Check Endpoints chuẩn Cloud-Native
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
