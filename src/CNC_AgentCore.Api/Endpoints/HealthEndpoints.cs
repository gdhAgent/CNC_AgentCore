// src/CNC_AgentCore.Api/Endpoints/HealthEndpoints.cs
// /health：4 项探测（db/llm/embedding/rerank）。缺 key → Healthy(skipped) 不影响 overall；真错 → Unhealthy。
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CNC_AgentCore.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse,
        }).WithName("Health").WithTags("system");
        return app;
    }

    private static Task WriteHealthResponse(HttpContext ctx, HealthReport report)
    {
        var checks = report.Entries.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, object?>
            {
                ["status"] = StatusStr(kv.Value.Status),
                ["ms"] = kv.Value.Duration.TotalMilliseconds,
                ["error"] = kv.Value.Exception?.Message,
                ["data"] = kv.Value.Data.Count > 0 ? kv.Value.Data : null,
            });

        var body = new Dictionary<string, object?>
        {
            ["status"] = StatusStr(report.Status),
            ["version"] = "1.0.0",
            ["checks"] = checks,
        };
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
    }

    private static string StatusStr(HealthStatus s) => s switch
    {
        HealthStatus.Healthy => "ok",
        HealthStatus.Unhealthy => "down",
        _ => "degraded",
    };
}
