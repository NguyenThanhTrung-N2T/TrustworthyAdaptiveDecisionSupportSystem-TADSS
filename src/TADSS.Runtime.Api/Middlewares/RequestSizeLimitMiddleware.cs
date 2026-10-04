using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Common;
using TADSS.Runtime.Api.ErrorHandling;
using TADSS.Runtime.Api.Options;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Giới hạn kích thước body (SEC-2), chạy trước xác thực để request quá lớn không cần khóa vẫn nhận 413. <c>Content-Length</c> lớn hơn
/// <see cref="SecurityOptions.MaxRequestBodyBytes"/> → 413 <c>PAYLOAD_TOO_LARGE</c> qua <see cref="ProblemResponseWriter"/>, không gọi bước kế tiếp
/// và không đọc body. Ngược lại đặt <see cref="IHttpMaxRequestBodySizeFeature.MaxRequestBodySize"/> để body chunked (không có <c>Content-Length</c>)
/// vượt trần thì Kestrel ném <see cref="BadHttpRequestException"/> 413, <see cref="ExceptionClassifier"/> map sang cùng mã. Request không có body không bị ảnh hưởng.
/// </summary>
public class RequestSizeLimitMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    private readonly long _maxBodyBytes = options.Value.MaxRequestBodyBytes;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > _maxBodyBytes)
        {
            await ProblemResponseWriter.WriteAsync(
                context,
                new ProblemResponse(
                    StatusCodes.Status413PayloadTooLarge,
                    ErrorCodes.PayloadTooLarge,
                    "Payload Too Large",
                    ExceptionClassifier.GenericDetail(StatusCodes.Status413PayloadTooLarge)),
                context.RequestAborted);
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = _maxBodyBytes;
        }

        await next(context);
    }
}
