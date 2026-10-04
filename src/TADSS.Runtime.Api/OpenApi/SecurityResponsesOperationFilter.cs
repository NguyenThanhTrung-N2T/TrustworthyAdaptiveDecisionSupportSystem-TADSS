using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using TADSS.Runtime.Api.ErrorHandling;

namespace TADSS.Runtime.Api.OpenApi;

/// <summary>
/// Mọi operation đều yêu cầu <c>X-Api-Key</c> (INTEGRATION_SPEC §3.2) nên cùng có phản hồi 401 <c>UNAUTHORIZED</c> dạng problem+json
/// kèm header <c>WWW-Authenticate</c>. Operation có body còn có phản hồi 413 <c>PAYLOAD_TOO_LARGE</c> (body vượt <c>Tadss:Security:MaxRequestBodyBytes</c>);
/// các phản hồi khác do controller khai báo.
/// </summary>
public class SecurityResponsesOperationFilter : IOperationFilter
{
    private const string UnauthorizedStatus = "401";
    private const string PayloadTooLargeStatus = "413";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var problemSchema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        operation.Responses[UnauthorizedStatus] = new OpenApiResponse
        {
            Description = "Thiếu hoặc sai X-Api-Key (UNAUTHORIZED), kèm header WWW-Authenticate.",
            Headers = new Dictionary<string, OpenApiHeader>
            {
                ["WWW-Authenticate"] = new OpenApiHeader
                {
                    Description = "Cách xác thực: ApiKey header=\"X-Api-Key\".",
                    Schema = new OpenApiSchema { Type = "string" }
                }
            },
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [ProblemResponseWriter.ContentType] = new OpenApiMediaType { Schema = problemSchema }
            }
        };

        if (operation.RequestBody is not null)
        {
            operation.Responses[PayloadTooLargeStatus] = new OpenApiResponse
            {
                Description = "Body request lớn hơn giới hạn cấu hình, mặc định 65536 byte (PAYLOAD_TOO_LARGE); kiểm tra trước xác thực.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [ProblemResponseWriter.ContentType] = new OpenApiMediaType { Schema = problemSchema }
                }
            };
        }
    }
}
