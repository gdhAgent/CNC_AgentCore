// Api/Endpoints/FeedbackEndpoints.cs —— /api/feedback + /api/suggestions（问答数据闭环）
// feedback：校验 trace 存在与 verdict/reason → 写 feedbacks + 回写 query_logs；verdict=-1 自动建 negative_feedback 建议
//            （suggested_type 按 detected_codes 推断 alarm/faq）。
// suggestions：list / 手动 create / resolve / approve / reject；approve 将内容录入知识库并向量化。
// 组级 RequireAuth（viewer+）。
using System.Text.Json;
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;

namespace CNC_AgentCore.Api.Endpoints;

public static class FeedbackEndpoints
{
    private static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "not_relevant", "wrong_answer", "incomplete", "outdated", "no_source", "other",
    };

    private static readonly HashSet<string> AllowedSuggestionStatuses = new(StringComparer.Ordinal)
    {
        "open", "in_progress", "resolved", "rejected",
    };

    private static readonly HashSet<string> AllowedManualSources = new(StringComparer.Ordinal)
    {
        "refused", "manual", "low_score",
    };

    private static readonly HashSet<string> AllowedSuggestedTypes = new(StringComparer.Ordinal)
    {
        "alarm", "faq", "manual_chunk", "maintenance_tip",
    };

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("feedback")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapPost("/feedback", SubmitFeedback).WithName("SubmitFeedback");
        g.MapGet("/suggestions", ListSuggestions).WithName("ListSuggestions");
        g.MapPost("/suggestions", CreateSuggestion).WithName("CreateSuggestion");
        g.MapPost("/suggestions/{id:long}/resolve", ResolveSuggestion).WithName("ResolveSuggestion");
        g.MapPost("/suggestions/{id:long}/reject", RejectSuggestion).WithName("RejectSuggestion");
        g.MapPost("/suggestions/{id:long}/approve", ApproveSuggestion).WithName("ApproveSuggestion");
        return app;
    }

    // ===== POST /api/feedback =====

    private static async Task<IResult> SubmitFeedback(
        FeedbackRequestDto req,
        IFeedbackRepository feedbacks,
        ISuggestionRepository suggestions,
        IQueryLogRepository queryLogs,
        HttpContext http)
    {
        // —— 入参校验 ——
        if (req.Verdict is not (1 or -1))
            throw new ApiException(422, "invalid_verdict", "verdict 必须为 1 或 -1");
        if (req.Reason is not null && !AllowedReasons.Contains(req.Reason))
            throw new ApiException(422, "invalid_reason", $"reason 必须为 {string.Join("/", AllowedReasons)}");

        // —— 1) trace_id 存在性 ——
        var info = await queryLogs.GetInfoByTraceAsync(req.TraceId, http.RequestAborted)
            ?? throw new ApiException(404, "trace_not_found", "trace_id 不存在，无法提交反馈");

        // —— 2) 写 log.feedbacks ——
        var feedbackId = await feedbacks.InsertAsync(new FeedbackRecord(
            TraceId: req.TraceId,
            QueryLogId: (int)info.Id,
            Verdict: req.Verdict,
            UserCode: req.UserCode,
            Reason: req.Reason,
            BadRefs: req.BadRefs?.ToArray(),
            Comment: req.Comment,
            Correction: req.Correction), http.RequestAborted);

        // —— 3) 回写 query_logs.feedback / feedback_note ——
        var note = !string.IsNullOrWhiteSpace(req.Comment) ? req.Comment : req.Reason;
        await feedbacks.UpdateQueryLogFeedbackAsync(req.TraceId, req.Verdict, note, http.RequestAborted);

        // —— verdict=-1 → 自动生成 kb_suggestion（有 detected_codes→alarm，否则 faq）——
        long? suggestionId = null;
        if (req.Verdict == -1)
        {
            var suggestedType = info.DetectedCodes.Count > 0 ? "alarm" : "faq";
            suggestionId = await suggestions.InsertAsync(new SuggestionRecord(
                Source: "negative_feedback",
                Question: info.RawQuery,
                TraceId: req.TraceId,
                SuggestedType: suggestedType,
                DraftContent: !string.IsNullOrWhiteSpace(req.Correction) ? req.Correction : req.Comment), http.RequestAborted);
        }

        return Results.Ok(new FeedbackResponseDto
        {
            Id = feedbackId,
            SuggestionId = suggestionId,
            Message = "ok",
        });
    }

    // ===== GET /api/suggestions =====

    private static async Task<IResult> ListSuggestions(
        ISuggestionRepository suggestions,
        HttpContext http)
    {
        var status = http.Request.Query["status"].ToString();
        if (!string.IsNullOrEmpty(status) && !AllowedSuggestionStatuses.Contains(status))
            throw new ApiException(422, "invalid_status", $"status 必须为 {string.Join("/", AllowedSuggestionStatuses)}");

        var limit = int.TryParse(http.Request.Query["limit"].ToString(), out var l) ? Math.Clamp(l, 1, 200) : 200;
        var offset = int.TryParse(http.Request.Query["offset"].ToString(), out var o) ? Math.Max(o, 0) : 0;

        var items = await suggestions.ListAsync(
            string.IsNullOrEmpty(status) ? null : status, limit, offset, http.RequestAborted);

        // /api/suggestions 直接返回裸数组 SuggestionItem[]（非包装对象）
        return Results.Ok(items.Select(ToDto).ToList());
    }

    private static SuggestionItemDto ToDto(SuggestionListItem s) => new()
    {
        Id = s.Id,
        Source = s.Source,
        TraceId = s.TraceId,
        Question = s.Question,
        SuggestedType = s.SuggestedType,
        DraftContent = s.DraftContent,
        Status = s.Status,
        ResolvedRef = ParseJsonObject(s.ResolvedRef),
        Handler = s.Handler,
        CreatedAt = s.CreatedAt,
    };

    private static Dictionary<string, object?>? ParseJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(raw);
        }
        catch
        {
            // JSONB 损坏时降级为 null，不阻塞列表
            return null;
        }
    }

    // ===== POST /api/suggestions =====

    private static async Task<IResult> CreateSuggestion(
        SuggestionCreateRequestDto req,
        ISuggestionRepository suggestions,
        IQueryLogRepository queryLogs,
        HttpContext http)
    {
        // —— 入参校验 ——
        if (!AllowedManualSources.Contains(req.Source))
            throw new ApiException(422, "invalid_source",
                $"source 必须为 {string.Join("/", AllowedManualSources)}（negative_feedback 由 POST /api/feedback 自动生成）");
        if (req.SuggestedType is not null && !AllowedSuggestedTypes.Contains(req.SuggestedType))
            throw new ApiException(422, "invalid_suggested_type",
                $"suggested_type 必须为 {string.Join("/", AllowedSuggestedTypes)}");

        // —— trace_id 存在性 ——
        var info = await queryLogs.GetInfoByTraceAsync(req.TraceId, http.RequestAborted)
            ?? throw new ApiException(404, "trace_not_found", "trace_id 不存在，无法提交建议");

        // —— 缺省字段补全（question/suggested_type 从 query_logs 推断）——
        var question = !string.IsNullOrWhiteSpace(req.Question) ? req.Question : info.RawQuery;
        var suggestedType = req.SuggestedType
            ?? (info.DetectedCodes.Count > 0 ? "alarm" : "faq");

        var id = await suggestions.InsertAsync(new SuggestionRecord(
            Source: req.Source,
            Question: question,
            TraceId: req.TraceId,
            SuggestedType: suggestedType,
            DraftContent: req.DraftContent), http.RequestAborted);

        return Results.Ok(new SuggestionCreateResponseDto
        {
            Id = id,
            Status = "open",
        });
    }

    // ===== POST /api/suggestions/{id}/resolve =====

    private static async Task<IResult> ResolveSuggestion(
        long id,
        ResolveSuggestionRequestDto req,
        ISuggestionRepository suggestions,
        HttpContext http)
    {
        var ok = await suggestions.ResolveAsync(id, req.ResolvedRef, req.Handler, http.RequestAborted);
        if (!ok)
        {
            // rowcount==0 两种可能：id 不存在 / 状态非 open
            if (!await suggestions.ExistsAsync(id, http.RequestAborted))
                throw new ApiException(404, "suggestion_not_found", $"suggestion id={id} 不存在");
            throw new ApiException(409, "invalid_state", "仅 open 状态的建议可解决");
        }
        return Results.Ok(new ResolveSuggestionResponseDto
        {
            Id = id,
            Status = "resolved",
        });
    }

    // ===== POST /api/suggestions/{id}/reject =====

    private static async Task<IResult> RejectSuggestion(
        long id,
        ISuggestionRepository suggestions,
        HttpContext http)
    {
        // handler 由 query string 传入
        var handler = http.Request.Query["handler"].ToString();
        var ok = await suggestions.RejectAsync(id, string.IsNullOrEmpty(handler) ? null : handler, http.RequestAborted);
        if (!ok)
        {
            if (!await suggestions.ExistsAsync(id, http.RequestAborted))
                throw new ApiException(404, "suggestion_not_found", $"suggestion id={id} 不存在");
            throw new ApiException(409, "invalid_state", "仅 open 状态的建议可拒绝");
        }
        return Results.Ok(new RejectSuggestionResponseDto
        {
            Id = id,
            Status = "rejected",
        });
    }

    // ===== POST /api/suggestions/{id}/approve =====

    private static async Task<IResult> ApproveSuggestion(
        long id,
        ApproveSuggestionRequestDto req,
        ISuggestionRepository suggestions,
        IKnowledgeEntryRepository knowledge,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory,
        HttpContext http)
    {
        if (req.EntryType is not ("faq" or "alarm"))
            throw new ApiException(422, "entry_type_not_supported",
                $"entry_type 必须为 'faq' 或 'alarm'；当前 '{req.EntryType}'");

        // —— 1) 校验 suggestion 状态 ——
        var sug = await suggestions.FetchDetailAsync(id, http.RequestAborted)
            ?? throw new ApiException(404, "suggestion_not_found", $"suggestion id={id} 不存在");
        if (sug.Status != "open")
            throw new ApiException(409, "invalid_state", $"仅 open 状态的建议可审核（当前 {sug.Status}）");

        var handler = string.IsNullOrWhiteSpace(req.CreatedBy) ? "E1024" : req.CreatedBy;

        if (req.EntryType == "faq")
        {
            // —— FAQ 路径 ——
            var title = (!string.IsNullOrWhiteSpace(req.Title) ? req.Title : sug.Question ?? "补录知识").Trim();
            var body = (req.Body ?? sug.DraftContent ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(body))
                throw new ApiException(422, "empty_body", "正文内容不能为空");

            var (docId, chunkId) = await knowledge.InsertFaqAsync(
                title, body, req.Brand, req.ModelScope?.ToArray(),
                source: "feedback-approved", createdBy: handler, http.RequestAborted);

            bool vectorized = false;
            try
            {
                var results = await embedding.GenerateAsync(new[] { $"{title}\n{body}" }, cancellationToken: http.RequestAborted);
                var first = results.FirstOrDefault();
                if (first is not null)
                {
                    var vec = first.Vector.ToArray();
                    vectorized = await knowledge.VectorizeChunkAsync(chunkId, vec, http.RequestAborted);
                }
            }
            catch
            {
                vectorized = false;
            }

            await suggestions.ResolveAsync(id,
                new Dictionary<string, object?>
                {
                    ["type"] = "faq",
                    ["doc_id"] = docId,
                    ["chunk_id"] = chunkId,
                },
                handler, http.RequestAborted);

            return Results.Ok(new ApproveSuggestionResponseDto
            {
                Id = id,
                Status = "resolved",
                EntryType = "faq",
                DocId = docId,
                ChunkId = chunkId,
                Vectorized = vectorized,
            });
        }
        else // entry_type == "alarm"

        {
            if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
                throw new ApiException(422, "missing_required", "alarm: code/name 必填");

            var brand = !string.IsNullOrWhiteSpace(req.AlarmBrand)
                ? req.AlarmBrand
                : (!string.IsNullOrWhiteSpace(req.Brand) ? req.Brand : "GENERIC");
            // code_norm 自动从 Code 算（KnowledgeEntryRepository.InsertAlarmAsync 内部处理）

            long alarmId;
            try
            {
                alarmId = await knowledge.InsertAlarmAsync(new CreateAlarmRequest(
                    Brand: brand,
                    Controller: req.Controller,
                    Code: req.Code,
                    Name: req.Name,
                    Description: req.Description ?? sug.DraftContent,
                    Cause: req.Cause,
                    Action: req.Action,
                    SafetyNote: req.SafetyNote,
                    Category: req.Category,
                    Severity: req.Severity,
                    CreatedBy: handler), http.RequestAborted);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("alarm 已存在"))
            {
                throw new ApiException(409, "alarm_exists", ex.Message);
            }

            // 后台 fire-and-forget 向量化
            KnowledgeEnqueueAlarmVectorize(alarmId, knowledge, embedding, loggerFactory.CreateLogger("ApproveAlarmVectorize"));

            await suggestions.ResolveAsync(id,
                new Dictionary<string, object?>
                {
                    ["type"] = "alarm",
                    ["id"] = alarmId,
                },
                handler, http.RequestAborted);

            return Results.Ok(new ApproveSuggestionResponseDto
            {
                Id = id,
                Status = "resolved",
                EntryType = "alarm",
                DocId = alarmId,   // 复用：alarm 场景下理解为 alarm_id
                ChunkId = 0,
                Vectorized = true, // 后台 fire-and-forget 触发，标志 true
            });
        }
    }

    private static void KnowledgeEnqueueAlarmVectorize(
        long alarmId, IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding, ILogger log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var text = await repo.GetAlarmVectorizeTextAsync(alarmId, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(text)) return;
                var result = await embedding.GenerateAsync(new[] { text }, cancellationToken: CancellationToken.None);
                if (result is null || result.Count == 0) return;
                var vec = result[0].Vector.ToArray();
                var ok = await repo.VectorizeAlarmAsync(alarmId, vec, CancellationToken.None);
                log.LogInformation("[approve/alarm] 向量化完成 id={Id} ok={Ok}", alarmId, ok);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[approve/alarm] 向量化失败 id={Id}", alarmId);
            }
        });
    }
}
