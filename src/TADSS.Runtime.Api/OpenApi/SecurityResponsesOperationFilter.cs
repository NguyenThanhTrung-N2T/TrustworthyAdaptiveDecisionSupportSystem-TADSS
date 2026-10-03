using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using TADSS.Runtime.Api.ErrorHandling;

namespace TADSS.Runtime.Api.OpenApi;

/// <summary>
/// Mọi operation đều yêu cầu <c>X-Api-Key</c> (INTEGRATION_SPEC §3.2) nên cùng có phản hồi 401 <c>UNAUTHORIZED</c> dạng problem+json
/// kèm header <c>WWW-Authenticate</c>; các phản hồi khác do controller khai báo.
/// </summary>
public class SecurityResponsesOperationFilter : IOperationFilter
{
    private const string UnauthorizedStatus = "401";

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
    }
}
