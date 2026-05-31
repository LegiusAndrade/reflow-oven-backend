using Microsoft.AspNetCore.HttpLogging;

namespace ReflowOven.Api.Middleware;

/// <summary>
/// HTTP-logging interceptor that drops the request/response <b>body</b> for the auth endpoints
/// (<c>/api/auth/*</c>) — those bodies carry passwords and tokens. Headers are still redacted by the
/// <c>AddHttpLogging</c> config (Authorization). Everything else logs method/path/query/status/body.
/// </summary>
public sealed class AuthRedactionInterceptor : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        if (logContext.HttpContext.Request.Path.StartsWithSegments("/api/auth"))
            logContext.LoggingFields &= ~(HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext) => ValueTask.CompletedTask;
}
