using Microsoft.EntityFrameworkCore;
using Shared.DecisionContractSDK.Common;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Configuration;

namespace TADSS.Runtime.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 1. DbContext: thời gian audit do DecisionService gán, không có interceptor ghi timestamp
        services.AddDbContext<TadssDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "tadss");
            });
        });
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        // 2. Health Checks có phân nhóm Tags (Liveness / Readiness)
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: connectionString ?? string.Empty,
                name: "Neon-PostgreSQL",
                tags: new[] { "ready", "db" });

        // 3. Lõi runtime: strategy registry, resilience, cache, audit store — nơi đăng ký duy nhất cho lõi
        services.AddDecisionRuntime(configuration);

        return services;
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        // Các ProblemDetails do framework dựng (vd. 415 khi Content-Type không phải JSON) cũng mang traceId = correlation id đã chuẩn hóa
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        // JSON v2 chỉ qua TadssJson.Apply (camelCase, phân biệt hoa thường, enum/thời gian chặt, trường lạ bị từ chối);
        // không thêm converter toàn cục nào vì sẽ làm enum v2 nhận chữ thường/chuỗi số (Apply ném lỗi nếu có)
        services.AddControllers()
            .AddJsonOptions(options => TadssJson.Apply(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options =>
            {
                // Mọi lỗi giải mã body đi qua GlobalExceptionHandler để có cùng một dạng problem+json
                options.InvalidModelStateResponseFactory = InvalidModelStateTranslator.Throw;
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v2", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "TADSS Decision Runtime API",
                Version = "v2",
                Description = "Decision Runtime của TADSS cho SmashCourt: đánh giá quyết định đặt sân (POST /api/v2/decisions) và tra cứu chi tiết quyết định (GET /api/v2/decisions/{decisionId}). Contract v2: INTEGRATION_SPEC."
            });

            // Load API XML documentation if present
            var apiXmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var apiXmlPath = Path.Combine(AppContext.BaseDirectory, apiXmlFile);
            if (File.Exists(apiXmlPath))
            {
                c.IncludeXmlComments(apiXmlPath);
            }

            // Load Shared SDK XML documentation if present
            var sdkXmlFile = "Shared.DecisionContractSDK.xml";
            var sdkXmlPath = Path.Combine(AppContext.BaseDirectory, sdkXmlFile);
            if (File.Exists(sdkXmlPath))
            {
                c.IncludeXmlComments(sdkXmlPath);
            }

            // Register operation filter for contract-defined headers
            c.OperationFilter<RequiredHeaderOperationFilter>();
        });

        return services;
    }
}
