using Shared.DecisionContractSDK.Common;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Lõi runtime: use case (Application), adapter hạ tầng (Infrastructure) và strategy — nơi đăng ký duy nhất cho lõi
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
                Version = "2.0.0-draft",
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

            // problem+json của contract mang thêm code/traceId...; chuẩn hóa xuống dòng để openapi-v2.yaml sinh ổn định giữa các máy
            c.SchemaFilter<ProblemDetailsSchemaFilter>();
            c.DocumentFilter<LineEndingDocumentFilter>();
        });

        return services;
    }
}
