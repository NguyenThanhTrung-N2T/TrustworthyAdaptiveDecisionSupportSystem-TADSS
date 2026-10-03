using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TADSS.Runtime.Api.OpenApi;

/// <summary>
/// Chuẩn hóa mọi mô tả/tóm tắt lấy từ XML doc về xuống dòng <c>\n</c>: nội dung XML doc giữ nguyên kiểu xuống dòng của file nguồn
/// (CRLF hay LF tùy máy/autocrlf), nên không chuẩn hóa thì <c>openapi-v2.yaml</c> sinh ra sẽ khác nhau giữa các máy.
/// </summary>
public class LineEndingDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Info.Description = Normalize(swaggerDoc.Info.Description);

        foreach (var operation in swaggerDoc.Paths.Values.SelectMany(path => path.Operations.Values))
        {
            operation.Summary = Normalize(operation.Summary);
            operation.Description = Normalize(operation.Description);
            foreach (var parameter in operation.Parameters)
            {
                parameter.Description = Normalize(parameter.Description);
            }

            if (operation.RequestBody is not null)
            {
                operation.RequestBody.Description = Normalize(operation.RequestBody.Description);
            }

            foreach (var response in operation.Responses.Values)
            {
                response.Description = Normalize(response.Description);
            }
        }

        foreach (var schema in swaggerDoc.Components.Schemas.Values)
        {
            NormalizeSchema(schema);
        }
    }

    private static void NormalizeSchema(OpenApiSchema schema)
    {
        schema.Description = Normalize(schema.Description);
        foreach (var property in schema.Properties.Values.Where(property => property.Reference is null))
        {
            NormalizeSchema(property);
        }

        if (schema.Items is { Reference: null } items)
        {
            NormalizeSchema(items);
        }

        if (schema.AdditionalProperties is { Reference: null } additional)
        {
            NormalizeSchema(additional);
        }
    }

    private static string? Normalize(string? text) => text?.Replace("\r\n", "\n").Replace('\r', '\n');
}
