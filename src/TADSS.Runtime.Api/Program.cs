using Serilog;
using TADSS.Application;
using TADSS.Infrastructure;
using TADSS.Runtime.Api.Composition;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddTadssTestingConfiguration(builder.Environment);

// Cấu hình Serilog
builder.Host.UseSerilog((context, services, configuration) =>
    SerilogTelemetryConfiguration.Apply(configuration.ReadFrom.Configuration(context.Configuration), context.Configuration, context.HostingEnvironment));

// Đăng ký Services: mỗi tầng tự đăng ký phần của mình
builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddStrategies(builder.Configuration, builder.Environment)
    .AddPresentation(builder.Configuration, builder.Environment);

var app = builder.Build();

await StartupTasks.RunAsync(app);

app.UseTadssRequestPipeline();

// Controller và health live/ready (không có /health gộp)
app.MapTadssEndpoints();

app.Run();

public partial class Program { }
