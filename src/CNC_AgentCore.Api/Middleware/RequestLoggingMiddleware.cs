// Api/Middleware/RequestLoggingMiddleware.cs —— 请求日志（method/path/status/耗时）
namespace CNC_AgentCore.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _log;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext http)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _next(http);
        }
        finally
        {
            sw.Stop();
            _log.LogInformation("{Method} {Path} -> {StatusCode} in {Ms}ms",
                http.Request.Method, http.Request.Path, http.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }
}
