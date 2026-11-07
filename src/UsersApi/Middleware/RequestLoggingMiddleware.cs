using System.Diagnostics;

namespace UsersApi.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();
        context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
        try { await next(context); }
        finally
        {
            logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} en {ElapsedMs} ms. RequestId={RequestId}",
                context.Request.Method, context.Request.Path, context.Response.StatusCode,
                timer.ElapsedMilliseconds, context.TraceIdentifier);
        }
    }
}
