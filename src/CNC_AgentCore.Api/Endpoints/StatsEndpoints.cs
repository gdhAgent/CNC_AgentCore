// Api/Endpoints/StatsEndpoints.cs —— GET /api/stats/top-faults（双源并行聚合 + 查询侧 enrich）
// 时间窗口：days 优先 → 回看 N 天；否则用 from_time/to_time（to_time 缺省 UtcNow）。
// 查询侧用 IStatsRepository.EnrichCodesAsync 补 name/severity/brand。
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/stats").WithTags("stats")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapGet("/top-faults", GetTopFaults).WithName("TopFaults");
        return app;
    }

    private static async Task<IResult> GetTopFaults(
        IStatsRepository stats,
        HttpContext http)
    {
        var q = http.Request.Query;

        // —— 1) 解析时间窗口 ——
        DateTimeOffset? fromTime = null;
        DateTimeOffset? toTime = null;
        int? days = null;

        var daysStr = q["days"].ToString();
        if (!string.IsNullOrEmpty(daysStr))
        {
            if (!int.TryParse(daysStr, out var d) || d < 1 || d > 365)
                throw new ApiException(422, "invalid_days", "days 必须为 1~365 的正整数");
            days = d;
        }

        var ftStr = q["from_time"].ToString();
        if (!string.IsNullOrEmpty(ftStr))
        {
            if (!DateTimeOffset.TryParse(ftStr, out var ft))
                throw new ApiException(422, "invalid_from_time", $"from_time 格式错误：{ftStr}");
            fromTime = ft;
        }
        var ttStr = q["to_time"].ToString();
        if (!string.IsNullOrEmpty(ttStr))
        {
            if (!DateTimeOffset.TryParse(ttStr, out var tt))
                throw new ApiException(422, "invalid_to_time", $"to_time 格式错误：{ttStr}");
            toTime = tt;
        }

        // days 优先
        if (days is not null)
        {
            var now = DateTimeOffset.UtcNow;
            fromTime = now.AddDays(-days.Value);
            toTime = now;
        }
        else
        {
            toTime ??= DateTimeOffset.UtcNow;
        }
        if (fromTime is not null && fromTime > toTime)
            throw new ApiException(422, "invalid_window", "from_time 必须早于或等于 to_time");

        var topN = int.TryParse(q["top_n"].ToString(), out var n) ? Math.Clamp(n, 1, 50) : 20;

        // —— 2) 并行 fetch 两个数据源 ——
        var queryTask = stats.FetchTopByQueryAsync(fromTime, toTime, topN, http.RequestAborted);
        var maintTask = stats.FetchTopByMaintenanceAsync(fromTime, toTime, topN, http.RequestAborted);
        await Task.WhenAll(queryTask, maintTask);
        var (queryItems, totalQuery) = queryTask.Result;
        var (maintItems, totalMaint) = maintTask.Result;

        // —— 3) 查询侧 enrich ——
        var queryCodes = queryItems.Select(i => i.CodeNorm).Distinct().ToList();
        var enrich = await stats.EnrichCodesAsync(queryCodes, http.RequestAborted);
        for (var i = 0; i < queryItems.Count; i++)
        {
            var it = queryItems[i];
            if (enrich.TryGetValue(it.CodeNorm, out var meta))
            {
                // record init-only 不能后赋值 → 用 `with` 表达式重建
                queryItems[i] = it with { Name = meta.Name, Severity = meta.Severity, Brand = meta.Brand };
            }
        }

        // —— 4) 组装响应 ——
        return Results.Ok(new TopFaultsResponseDto
        {
            Window = new TopFaultsWindowDto
            {
                FromTime = fromTime,
                ToTime = toTime.Value,
                Days = days,
            },
            TotalQueryLogs = totalQuery,
            TotalMaintenanceLogs = totalMaint,
            ByQuery = queryItems.Select(ToDto).ToList(),
            ByMaintenance = maintItems.Select(ToDto).ToList(),
        });
    }

    private static TopFaultItemDto ToDto(TopFaultAggItem a) => new()
    {
        CodeNorm = a.CodeNorm,
        Count = a.Count,
        Name = a.Name,
        Severity = a.Severity,
        Brand = a.Brand,
        LastSeenAt = a.LastSeenAt,
    };
}
