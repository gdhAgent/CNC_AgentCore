// Api/Endpoints/TraceEndpoints.cs —— GET /api/trace/{traceId}（单条问答 trace 详情 + steps）
//                          + GET /api/logs（问答日志列表，支持过滤/分页）。viewer+。
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class TraceEndpoints
{
    // /api/logs 的 route 白名单，需与 query_logs.route 的 CHECK 约束一致
    private static readonly HashSet<string> AllowedRoutes = new(StringComparer.Ordinal)
    {
        "exact_code", "hybrid", "refused", "agent", "rag_fallback",
    };

    public static IEndpointRouteBuilder MapTraceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("trace")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapGet("/trace/{traceId:guid}", GetTrace).WithName("GetTrace");
        g.MapGet("/logs", ListLogs).WithName("ListLogs");
        return app;
    }

    private static async Task<IResult> GetTrace(
        Guid traceId,
        IQueryLogRepository queryLogs,
        IFeedbackRepository feedbacks,
        HttpContext http)
    {
        // —— 1) 主记录 + steps 并行 fetch ——
        var detailTask = queryLogs.GetFullDetailAsync(traceId, http.RequestAborted);
        var stepsTask = queryLogs.GetTraceStepsAsync(traceId, http.RequestAborted);
        var verdictTask = feedbacks.GetLatestVerdictByTraceAsync(traceId, http.RequestAborted);
        await Task.WhenAll(detailTask, stepsTask, verdictTask);

        var detail = detailTask.Result
            ?? throw new ApiException(404, "trace_not_found", $"trace_id={traceId} 不存在");
        var steps = stepsTask.Result;
        var verdict = verdictTask.Result;

        // —— 2) 组装响应 ——
        return Results.Ok(new TraceResponseDto
        {
            TraceId = traceId,
            Question = detail.RawQuery,
            Route = detail.Route,
            Refused = detail.Refused,
            DetectedCodes = detail.DetectedCodes,
            Answer = detail.Answer,
            LatencyMs = detail.LatencyMs,
            LatencyBreakdown = detail.LatencyBreakdown,
            ToolCalls = detail.ToolCalls,
            Feedback = verdict,
            CreatedAt = detail.CreatedAt,
            Steps = steps.Select(ToStepDto).ToList(),
            RankingComparison = BuildRankingComparison(steps),
        });
    }

    // ===== GET /api/logs =====

    private static async Task<IResult> ListLogs(
        [FromQuery] bool? refused,
        [FromQuery] string? route,
        [FromQuery] string? feedback,
        [FromQuery(Name = "user_code")] string? userCode,
        [FromQuery] string? q,
        [FromQuery(Name = "from_time")] DateTimeOffset? fromTime,
        [FromQuery(Name = "to_time")] DateTimeOffset? toTime,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IQueryLogRepository queryLogs,
        HttpContext http)
    {
        var lim = Math.Clamp(limit ?? 20, 1, 100);
        var off = Math.Max(offset ?? 0, 0);

        // feedback 转 (int? + bool feedbackAny)：1/-1 → int；"any" → any；其他非空 → 422
        int? fb = null;
        bool fbAny = false;
        if (!string.IsNullOrEmpty(feedback))
        {
            if (feedback == "1") fb = 1;
            else if (feedback == "-1") fb = -1;
            else if (feedback == "any") fbAny = true;
            else
                throw new ApiException(422, "invalid_feedback", "feedback 仅支持 1 / -1 / any");
        }

        if (route is not null && !AllowedRoutes.Contains(route))
            throw new ApiException(422, "invalid_route",
                $"route 必须是 {string.Join('/', AllowedRoutes)} 之一");

        var query = new QueryLogListQuery(
            Refused: refused,
            Feedback: fb,
            FeedbackAny: fbAny,
            Route: route,
            UserCode: userCode,
            Q: q,
            FromTime: fromTime,
            ToTime: toTime,
            Limit: lim,
            Offset: off);

        var (items, total) = await queryLogs.ListAsync(query, http.RequestAborted);
        return Results.Ok(new LogListResponseDto
        {
            Items = items.Select(ToItemDto).ToList(),
            Total = total,
            Limit = lim,
            Offset = off,
        });
    }

    private static LogItemDto ToItemDto(QueryLogListItem r) => new()
    {
        Id = r.Id,
        TraceId = r.TraceId,
        RawQuery = r.RawQuery,
        Route = r.Route,
        Refused = r.Refused,
        Feedback = r.Feedback,
        LatencyMs = r.LatencyMs,
        UserCode = r.UserCode,
        CreatedAt = r.CreatedAt,
    };

    private static TraceStepItemDto ToStepDto(TraceStepRow s) => new()
    {
        Seq = s.Seq,
        Step = s.Step,
        Status = s.Status,
        StartedAt = s.StartedAt,
        Ms = s.Ms,
        Input = s.Input,
        Output = s.Output,
        Note = s.Note,
    };

    /// <summary>
    /// 从 rrf_fusion（ranks_by_channel + rrf rank）与 rerank（rerank rank）推导三路排名表。
    /// 只有 hybrid 路径有 rrf_fusion 步骤；exact_code / refused 路径返回空表。
    /// </summary>
    private static List<RankingRowDto> BuildRankingComparison(List<TraceStepRow> steps)
    {
        var rrf = steps.FirstOrDefault(s => s.Step == "rrf_fusion");
        var rerank = steps.FirstOrDefault(s => s.Step == "rerank");
        if (rrf is null || !rrf.Output.TryGetValue("candidates", out var candsObj) || candsObj is not List<object?> candidates)
            return new List<RankingRowDto>();

        // rerank 步的 candidates → [(type,id)] → rank 字典
        var rerankMap = new Dictionary<(string, long), int>();
        if (rerank is not null && rerank.Output.TryGetValue("candidates", out var rcObj) && rcObj is List<object?> rcands)
        {
            for (var i = 0; i < rcands.Count; i++)
            {
                if (rcands[i] is not Dictionary<string, object?> d) continue;
                var key = (GetStr(d, "type"), GetLong(d, "id"));
                if (!rerankMap.ContainsKey(key))
                    rerankMap[key] = d.TryGetValue("rank", out var r) && r is int ri ? ri : i + 1;
            }
        }

        var rows = new List<RankingRowDto>();
        foreach (var obj in candidates)
        {
            if (obj is not Dictionary<string, object?> c) continue;
            var rbc = c.TryGetValue("ranks_by_channel", out var rbcObj) && rbcObj is Dictionary<string, object?> rbcd
                ? rbcd : new Dictionary<string, object?>();
            var key = (GetStr(c, "type"), GetLong(c, "id"));
            rows.Add(new RankingRowDto
            {
                Type = key.Item1,
                Id = key.Item2,
                Title = GetStr(c, "title"),
                VectorRank = GetInt(rbc, "vector"),
                FulltextRank = GetInt(rbc, "fulltext"),
                RrfRank = c.TryGetValue("rank", out var rk) && rk is int rki ? rki : null,
                RerankRank = rerankMap.TryGetValue(key, out var rr) ? rr : null,
                Final = rerankMap.ContainsKey(key),
            });
        }
        return rows;
    }

    private static string GetStr(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    private static long GetLong(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is long l ? l
         : v is int i ? i
         : 0L;

    private static int? GetInt(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is int i ? i : null;
}
