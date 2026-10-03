using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TADSS.Runtime.Api.OpenApi;

/// <summary>
/// Bổ sung vào schema <see cref="ProblemDetails"/> các trường mở rộng mà <c>GlobalExceptionHandler</c> luôn/đôi khi trả trong
/// problem+json của contract (INTEGRATION_SPEC §3.8): <c>code</c>, <c>traceId</c>, <c>retryAfterSeconds</c>, <c>errors</c>.
/// Swashbuckle chỉ thấy các thuộc tính khai báo của <see cref="ProblemDetails"/> nên các trường này nằm trong <c>Extensions</c> sẽ bị thiếu.
/// </summary>
public class ProblemDetailsSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ProblemDetails))
        {
            return;
        }

        schema.Properties["code"] = new OpenApiSchema
        {
            Type = "string",
            Description = "Mã lỗi của contract v2 (INTEGRATION_SPEC §3.8); không có ở lỗi do framework dựng như 415."
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = "string",
            Description = "Correlation id của request (trùng header X-Correlation-ID)."
        };
        schema.Properties["retryAfterSeconds"] = new OpenApiSchema
        {
            Type = "integer",
            Format = "int32",
            Description = "Số giây nên chờ trước khi thử lại (kèm header Retry-After); chỉ có ở 503/504."
        };
        schema.Properties["errors"] = new OpenApiSchema
        {
            Type = "object",
            AdditionalProperties = new OpenApiSchema
            {
                Type = "array",
                Items = new OpenApiSchema { Type = "string" }
            },
            Description = "Lỗi theo trường (tên trường -> danh sách thông điệp); chỉ có khi lỗi gắn với một trường."
        };
    }
}
