// src/CNC_AgentCore.Api/Middleware/TraceIdMiddleware.cs
// 生成 trace_id（UUID）→ 注入到 HttpContext.Items["trace_id"] + 响应头 X-Trace-Id

namespace CNC_AgentCore.Api.Middleware;

public sealed class TraceIdMiddleware
{
    public const string TraceIdKey = "trace_id";
    public const string TraceIdHeader = "X-Trace-Id";

    private readonly RequestDelegate _next;

    public TraceIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // 优先用上游传过来的（链路追踪），否则生成新 UUID
        var traceId = context.Request.Headers.TryGetValue(TraceIdHeader, out var inbound)
            && !string.IsNullOrWhiteSpace(inbound)
            ? inbound.ToString()
            : Guid.NewGuid().ToString("D");

        context.Items[TraceIdKey] = traceId;
        context.Response.Headers[TraceIdHeader] = traceId;

        await _next(context);
    }
}
