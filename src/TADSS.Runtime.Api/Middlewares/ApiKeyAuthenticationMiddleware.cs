using System.Diagnostics;
using Serilog.Context;
using Shared.DecisionContractSDK.Common;
using TADSS.Application.Common;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.ErrorHandling;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Xác thực <c>X-Api-Key</c> cho <c>/api/*</c> (spec 4.3.2): thiếu, sai hoặc nhiều giá trị khóa → 401 <c>UNAUTHORIZED</c> kèm <c>WWW-Authenticate</c>, không gọi bước kế tiếp.
/// Health check được miễn ở mọi môi trường, Swagger chỉ được miễn ở Development. Khóa đúng thì <c>ClientId</c> được gắn vào
/// <see cref="ApiClientIdentity"/>, log scope và <see cref="Activity"/>. Không bao giờ log hay đưa vào problem giá trị khóa hoặc mã băm của khóa.
/// </summary>
public class ApiKeyAuthenticationMiddleware(
    RequestDelegate next,
    ApiKeyVerifier verifier,
    IHostEnvironment environment,
    ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    private const string ChallengeValue = $"ApiKey header=\"{HttpHeaderNames.ApiKey}\"";
    private const string UnauthorizedDetail = "Thiếu hoặc sai API key. Gửi khóa hợp lệ trong header X-Api-Key.";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!SecurityPaths.IsApi(path)
            || SecurityPaths.IsHealth(path)
            || (environment.IsDevelopment() && SecurityPaths.IsSwagger(path)))
        {
            await next(context);
            return;
        }

        // Nhiều giá trị X-Api-Key (nhiều header hoặc dòng lặp) là mơ hồ nên bị từ chối như thiếu khóa, không chọn giá trị nào
        var presentedKeys = context.Request.Headers[HttpHeaderNames.ApiKey];
        if (presentedKeys.Count != 1 || !verifier.TryAuthenticate(presentedKeys[0], out var clientId))
        {
            logger.LogInformation("Từ chối request {Method} {Path}: thiếu, sai hoặc lặp {Header}.", context.Request.Method, path, HttpHeaderNames.ApiKey);
            context.Response.Headers.WWWAuthenticate = ChallengeValue;
            await ProblemResponseWriter.WriteAsync(
                context,
                new ProblemResponse(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Unauthorized", UnauthorizedDetail),
                context.RequestAborted);
            return;
        }

        ApiClientIdentity.Set(context, clientId);
        Activity.Current?.SetTag(TadssActivitySource.TagClientId, clientId);
        using (LogContext.PushProperty("ClientId", clientId))
        {
            logger.LogDebug("Request được xác thực bằng API key của client.");
            await next(context);
        }
    }
}
