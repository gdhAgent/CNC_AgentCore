// Api/Endpoints/QueryEndpoints.cs —— /api/cnc/query（同步）+ /api/cnc/query/stream（SSE 流式）
// SSE 事件序列：retrieval → tool* → delta* → done；落库失败不阻塞流。
using System.Text;
using System.Text.Json;
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Application.Agent;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cnc").WithTags("query");
        g.MapPost("/query", HandleQuery).WithName("Query");
        g.MapPost("/query/stream", HandleQueryStream).WithName("QueryStream");
        return app;
    }

    // ===== 同步 /query =====

    private static async Task<IResult> HandleQuery(
        QueryRequest req, IRetrievalService retrieval, RetrievalServiceConfig baseCfg,
        IQueryLogRepository logRepo, HttpContext http)
    {
        var cfg = new RetrievalServiceConfig
        {
            VectorTopN = baseCfg.VectorTopN,
            FulltextTopN = baseCfg.FulltextTopN,
            RrfTopN = baseCfg.RrfTopN,
            RerankTopN = Math.Clamp(req.TopN, 1, 20),
            RerankThreshold = baseCfg.RerankThreshold,
            Brand = req.Brand,
            MachineModel = req.MachineModel,
        };

        var result = await retrieval.RunQueryAsync(req.Query, cfg, http.RequestAborted);

        await TryLogAsync(logRepo, req, result, http.RequestAborted);

        return Results.Ok(MapResponse(result));
    }

    private static async Task TryLogAsync(IQueryLogRepository logRepo, QueryRequest req, QueryResult result, CancellationToken ct)
    {
        try
        {
            var logId = await logRepo.InsertAsync(new QueryLogRecord(
                result.TraceId,
                RawQuery: req.Query,
                Route: result.Route,
                DetectedCodes: result.DetectedCodes,
                RetrievedSnapshot: result.RetrievedSnapshot,
                TopScore: result.Topk.Count > 0 ? result.Topk[0].Score : null,
                Refused: result.Refused,
                LatencyMs: result.Timing.Total,
                LatencyBreakdown: result.Timing.AsDict(),
                SessionId: req.SessionId,
                UserCode: req.UserCode), ct);
            await logRepo.InsertTraceStepsAsync(logId, result.TraceId, result.TraceSteps, ct);
        }
        catch
        {
            // 落库失败不阻塞返回
        }
    }

    private static QueryResponse MapResponse(QueryResult result) => new()
    {
        TraceId = result.TraceId.ToString(),
        Route = result.Route,
        DetectedCodes = result.DetectedCodes,
        Refused = result.Refused,
        RefusedReason = result.RefusedReason,
        Topk = result.Topk.Select((h, i) => ToTopKItem(i + 1, h)).ToList(),
        SuggestHits = result.SuggestHits.Select(h => ToTopKItem(0, h)).ToList(),
        ToolCalls = new(),
        Timing = new TimingInfo
        {
            Embed = result.Timing.Embed,
            CodeExtract = result.Timing.CodeExtract,
            ExactMatch = result.Timing.ExactMatch,
            VectorRecall = result.Timing.VectorRecall,
            FulltextRecall = result.Timing.FulltextRecall,
            RrfFusion = result.Timing.RrfFusion,
            Rerank = result.Timing.Rerank,
            ThresholdGate = result.Timing.ThresholdGate,
            Total = result.Timing.Total,
        },
    };

    private static TopKItem ToTopKItem(int refNo, Hit h) => new()
    {
        Ref = refNo,
        Type = h.Type,
        Id = h.Id,
        Score = Math.Round(h.Score, 4),
        Channel = string.IsNullOrEmpty(h.Channel) ? new() : new List<string> { h.Channel },
        Title = h.Title,
        Source = h.Source,
        Content = h.Content,
        CodeNorm = h.Type == "alarm" && h.Extra.TryGetValue("code_norm", out var c) ? c?.ToString() : null,
    };

    // ===== 流式 /query/stream（SSE） =====

    private static IResult HandleQueryStream(QueryRequest req, IAgentRouter router, IQueryLogRepository logRepo, HttpContext http)
    {
        var ct = http.RequestAborted;
        http.Response.Headers["Cache-Control"] = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        return new SseStreamResult(async (Stream stream, CancellationToken _) =>
        {
            Dictionary<string, object?>? lastRetrieval = null;
            await foreach (var ev in router.RunStreamAsync(req.Query, ct))
            {
                switch (ev.Kind)
                {
                    case "retrieval":
                        lastRetrieval = ev.Data;
                        await WriteSseAsync(stream, "retrieval", ev.Data, ct);
                        break;
                    case "tool":
                        await WriteSseAsync(stream, "tool", new Dictionary<string, object?>
                        {
                            ["name"] = Get(ev.Data, "name"),
                            ["args"] = Get(ev.Data, "args"),
                            ["ok"] = Get(ev.Data, "ok"),
                            ["ms"] = Get(ev.Data, "ms"),
                        }, ct);
                        break;
                    case "delta":
                        await WriteSseAsync(stream, "delta", new Dictionary<string, object?>
                        {
                            ["text"] = Get(ev.Data, "text") ?? "",
                        }, ct);
                        break;
                    case "done":
                        await TryLogStreamAsync(logRepo, req, ev.Result, lastRetrieval, ct);
                        await WriteSseAsync(stream, "done", ev.Data, ct);
                        return;
                }
            }
        });
    }

    private static async Task TryLogStreamAsync(IQueryLogRepository logRepo, QueryRequest req,
        AgentResult? result, Dictionary<string, object?>? lastRetrieval, CancellationToken ct)
    {
        if (result is null) return;
        try
        {
            var detected = new List<string>();
            if (lastRetrieval?.TryGetValue("detected_codes", out var dc) == true && dc is List<string> dcs)
                detected = dcs;

            var logId = await logRepo.InsertAsync(new QueryLogRecord(
                result.TraceId,
                RawQuery: req.Query,
                Route: result.Route,
                DetectedCodes: detected,
                RetrievedSnapshot: SnapshotFromRetrieval(lastRetrieval),
                TopScore: MaxScoreFromRetrieval(lastRetrieval),
                Refused: result.Refused,
                LatencyMs: result.TotalMs,
                LatencyBreakdown: new Dictionary<string, int> { ["total"] = result.TotalMs },
                Answer: result.Answer,
                SessionId: req.SessionId,
                UserCode: req.UserCode,
                ToolCalls: result.ToolCalls), ct);
            await logRepo.InsertTraceStepsAsync(logId, result.TraceId, result.TraceSteps, ct);
        }
        catch
        {
            // 落库失败不阻塞 SSE
        }
    }

    private static async Task WriteSseAsync(Stream stream, string eventName, Dictionary<string, object?>? data, CancellationToken ct)
    {
        var json = data is null ? "{}" : JsonSerializer.Serialize(data);
        var frame = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(frame);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static List<Dictionary<string, object?>> SnapshotFromRetrieval(Dictionary<string, object?>? r)
    {
        var snap = new List<Dictionary<string, object?>>();
        if (r is null || !r.TryGetValue("topk", out var topk) || topk is not IEnumerable<object?> list) return snap;
        foreach (var item in list)
        {
            if (item is not Dictionary<string, object?> d) continue;
            snap.Add(new Dictionary<string, object?>
            {
                ["type"] = Get(d, "type"),
                ["id"] = Get(d, "id"),
                ["score"] = Get(d, "score"),
                ["channel"] = Get(d, "channel"),
                ["rank"] = Get(d, "ref"),
            });
        }
        return snap;
    }

    private static double? MaxScoreFromRetrieval(Dictionary<string, object?>? r)
    {
        if (r is null || !r.TryGetValue("topk", out var topk) || topk is not IEnumerable<object?> list) return null;
        double? mx = null;
        foreach (var item in list)
        {
            if (item is Dictionary<string, object?> d && Get(d, "score") is double sc)
                mx = mx is null || sc > mx ? sc : mx;
        }
        return mx;
    }

    private static object? Get(Dictionary<string, object?>? d, string key)
        => d is not null && d.TryGetValue(key, out var v) ? v : null;
}

/// <summary>手写 SSE 写流 IResult（Results.Stream 的 Func 重载不稳定，自定义最可控）。</summary>
internal sealed class SseStreamResult : IResult
{
    private readonly Func<Stream, CancellationToken, Task> _write;

    public SseStreamResult(Func<Stream, CancellationToken, Task> write) => _write = write;

    public async Task ExecuteAsync(HttpContext http)
    {
        http.Response.ContentType = "text/event-stream";
        await _write(http.Response.Body, http.RequestAborted);
    }
}
