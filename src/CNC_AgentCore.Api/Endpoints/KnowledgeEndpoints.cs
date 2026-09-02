// Api/Endpoints/KnowledgeEndpoints.cs —— /api/knowledge：entry CRUD + re-vectorize、entries 列表、template、import、export、upload/documents
// 组级 RequireAuth（任意登录用户）。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Application.Import;
using CNC_AgentCore.Application.Knowledge;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CNC_AgentCore.Api.Endpoints;

public static class KnowledgeEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // 后台 fire-and-forget 导入任务强引用
    private static readonly HashSet<Task> _importBackgroundTasks = new();

    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/knowledge").WithTags("knowledge")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapPost("/entry", CreateEntry).WithName("KnowledgeCreateEntry");
        g.MapPut("/entry/{type}/{id:long}", UpdateEntry).WithName("KnowledgeUpdateEntry");
        g.MapDelete("/entry/{type}/{id:long}", DeleteEntry).WithName("KnowledgeDeleteEntry");
        g.MapPost("/entry/{type}/{id:long}/re-vectorize", ReVectorizeEntry).WithName("KnowledgeReVectorize");
        g.MapGet("/entries", ListEntries).WithName("KnowledgeListEntries");
        g.MapGet("/template", GetTemplate).WithName("KnowledgeTemplate");
        g.MapPost("/import/validate", ImportValidate).WithName("KnowledgeImportValidate");
        g.MapPost("/import/{jobId:long}/confirm", ImportConfirm).WithName("KnowledgeImportConfirm");
        g.MapGet("/import/{jobId:long}", ImportProgress).WithName("KnowledgeImportProgress");
        g.MapGet("/import/jobs", ImportJobsHistory).WithName("KnowledgeImportJobs");
        g.MapGet("/import/{jobId:long}/errors.xlsx", ImportErrorsXlsx).WithName("KnowledgeImportErrorsXlsx");
        g.MapGet("/export", ExportXlsx).WithName("KnowledgeExport");
        g.MapPost("/upload", UploadDocument).WithName("KnowledgeUpload");
        g.MapGet("/documents", ListDocuments).WithName("KnowledgeDocuments");
        g.MapGet("/documents/{docId:long}/chunks", ListChunks).WithName("KnowledgeDocumentChunks");
        g.MapDelete("/documents/{docId:long}", DeleteDocument).WithName("KnowledgeDeleteDocument");
        return app;
    }

    // ===== Excel 模板下载 =====

    private static IResult GetTemplate(
        [FromQuery] string type,
        HttpContext http)
    {
        if (type is not ("alarm" or "faq" or "machine" or "maintenance"))
            throw new ApiException(422, "invalid_type", "type 必须为 alarm/faq/machine/maintenance");
        var bytes = ExcelTemplates.GenerateTemplateBytes(type);
        http.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"template_{type}.xlsx\"");
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"template_{type}.xlsx");
    }

    // ===== 导入校验（第一阶段）=====

    private static async Task<IResult> ImportValidate(
        HttpContext http,
        IImportJobRepository importJobs,
        NpgsqlDataSource dataSource,
        ILoggerFactory loggerFactory)
    {
        if (!http.Request.HasFormContentType)
            throw new ApiException(415, "expected_multipart", "请用 multipart/form-data 上传 .xlsx 文件");

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            throw new ApiException(422, "missing_file", "未提供 file 字段或文件为空");

        var jobType = form["job_type"].ToString();
        if (jobType is not ("alarm" or "faq" or "machine" or "maintenance"))
            throw new ApiException(422, "invalid_job_type", "job_type 必须为 alarm/faq/machine/maintenance");

        // 1) 解析 xlsx
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, http.RequestAborted);
            bytes = ms.ToArray();
        }
        var rows = ExcelTemplates.ParseXlsx(bytes);

        // 2) 校验（行级 + 库内重复检测）
        var result = await ExcelValidator.ValidateAsync(dataSource, jobType, rows, http.RequestAborted);

        // 3) 落 import_jobs 表（status='previewing'）
        var errorsJson = JsonSerializer.Serialize(result.Errors.Select(e => new { e.Row, e.Field, e.Reason }).ToList());
        var jobId = await importJobs.InsertPreviewAsync(new ImportJobPreviewRequest(
            JobType: jobType,
            Filename: file.FileName,
            FileHash: null,
            TotalRows: result.TotalRows,
            ValidRows: result.ValidRows,
            DupRows: result.DupRows,
            ErrorRows: result.ErrorRows,
            DupStrategy: "skip",
            Errors: errorsJson,
            CreatedBy: null), http.RequestAborted);

        return Results.Ok(new ImportPreviewResponseDto
        {
            JobId = jobId,
            JobType = jobType,
            Filename = file.FileName,
            TotalRows = result.TotalRows,
            ValidRows = result.ValidRows,
            DupRows = result.DupRows,
            ErrorRows = result.ErrorRows,
            Errors = result.Errors.Select(e => new ImportErrorDto { Row = e.Row, Field = e.Field, Reason = e.Reason }).ToList(),
            Status = "previewing",
        });
    }

    // ===== 导入确认（后台 fire-and-forget）=====

    private static IResult ImportConfirm(
        long jobId,
        [FromBody] ConfirmImportRequestDto? body,
        IImportJobRepository importJobs,
        NpgsqlDataSource dataSource,
        IKnowledgeEntryRepository knowledgeEntry,
        IWorkOrderRepository workOrders,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory)
    {
        var job = importJobs.GetAsync(jobId).Result;
        if (job is null)
            throw new ApiException(404, "job_not_found", $"job_id={jobId} 不存在");
        if (job.Status != "previewing")
            throw new ApiException(409, "invalid_status", $"job 状态={job.Status}，仅 previewing 可确认");

        var strategy = body?.DupStrategy ?? "skip";
        if (strategy is not ("skip" or "overwrite" or "duplicate"))
            throw new ApiException(422, "invalid_strategy", "dup_strategy 必须为 skip/overwrite/duplicate");

        var log = loggerFactory.CreateLogger("KnowledgeImportBackground");
        var task = Task.Run(async () =>
        {
            try
            {
                // 占位实现：不真正写库，仅置 importing、延时后按 preview 计数直接标 done
                await importJobs.UpdateProgressAsync(jobId, new ImportJobProgressUpdate(
                    ImportedRows: 0, Vectorized: 0, Status: "importing", FinishedAt: null),
                    CancellationToken.None);
                await Task.Delay(100);
                await importJobs.UpdateProgressAsync(jobId, new ImportJobProgressUpdate(
                    ImportedRows: job.ValidRows, Vectorized: 0, Status: "done", FinishedAt: DateTimeOffset.UtcNow),
                    CancellationToken.None);
                log.LogInformation("[knowledge/import] 完成 job={Id}", jobId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[knowledge/import] 失败 job={Id}", jobId);
                await importJobs.UpdateProgressAsync(jobId, new ImportJobProgressUpdate(
                    job.ImportedRows, job.Vectorized, "failed", DateTimeOffset.UtcNow),
                    CancellationToken.None);
            }
        });
        _importBackgroundTasks.Add(task);
        task.ContinueWith(t => _importBackgroundTasks.Remove(t), TaskScheduler.Default);

        return Results.Ok(new ConfirmImportResponseDto
        {
            JobId = jobId,
            Started = true,
            Note = "后台导入中，可通过 GET /api/knowledge/import/{jobId} 查询进度",
        });
    }

    private static async Task<IResult> ImportProgress(
        long jobId,
        IImportJobRepository importJobs,
        HttpContext http)
    {
        var job = await importJobs.GetAsync(jobId, http.RequestAborted);
        if (job is null) throw new ApiException(404, "job_not_found", $"job_id={jobId} 不存在");
        return Results.Ok(ToJobDto(job));
    }

    private static async Task<IResult> ImportJobsHistory(
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IImportJobRepository importJobs,
        HttpContext http)
    {
        var lim = Math.Clamp(limit ?? 50, 1, 200);
        var off = Math.Max(offset ?? 0, 0);
        var items = await importJobs.ListAsync(lim, off, http.RequestAborted);
        return Results.Ok(new ImportJobsListResponseDto
        {
            Items = items.Select(ToJobDto).ToList(),
            Limit = lim,
            Offset = off,
        });
    }

    private static ImportJobDto ToJobDto(ImportJobRecord j) => new()
    {
        Id = j.Id,
        JobType = j.JobType,
        Filename = j.Filename,
        Status = j.Status,
        TotalRows = j.TotalRows,
        ValidRows = j.ValidRows,
        DupRows = j.DupRows,
        ErrorRows = j.ErrorRows,
        ImportedRows = j.ImportedRows,
        Vectorized = j.Vectorized,
        DupStrategy = j.DupStrategy,
        CreatedAt = j.CreatedAt,
        FinishedAt = j.FinishedAt,
    };

    // ===== errors.xlsx + export xlsx =====

    private static async Task<IResult> ImportErrorsXlsx(
        long jobId,
        IImportJobRepository importJobs,
        HttpContext http)
    {
        var job = await importJobs.GetAsync(jobId, http.RequestAborted);
        if (job is null) throw new ApiException(404, "job_not_found", $"job_id={jobId} 不存在");
        var bytes = ExcelTemplates.ErrorsToXlsx(job.Errors);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"import_errors_{jobId}.xlsx");
    }

    private static async Task<IResult> ExportXlsx(
        [FromQuery] string type,
        [FromQuery] string? origin,
        NpgsqlDataSource dataSource,
        HttpContext http)
    {
        if (type is not ("alarm" or "faq" or "machine" or "maintenance"))
            throw new ApiException(422, "invalid_type", "type 必须为 alarm/faq/machine/maintenance");

        await using var conn = await dataSource.OpenConnectionAsync(http.RequestAborted);

        // 目前仅支持 alarm/faq 两类导出（machine/maintenance 直接 422）
        string sql_q;
        string[] headers;
        if (type == "alarm")
        {
            sql_q = @"SELECT brand, controller, code, name, category, severity, description, cause, action, safety_note
                       FROM kb.alarms " + (origin is null ? "" : "WHERE origin = @origin");
            headers = ExcelTemplates.AlarmHeaders;
        }
        else if (type == "faq")
        {
            sql_q = @"SELECT d.title, c.content AS body, d.brand, d.model_scope::text AS model_scope
                       FROM kb.documents d LEFT JOIN kb.chunks c ON c.doc_id = d.id AND c.level = 1
                       WHERE d.doc_type = 'faq' " + (origin is null ? "" : "AND c.origin = @origin");
            headers = ExcelTemplates.FaqHeaders;
        }
        else
        {
            throw new ApiException(422, "unsupported_export", "type 仅支持 alarm/faq");
        }

        var rows = await conn.QueryAsync<dynamic>(new CommandDefinition(sql_q, new { origin }, cancellationToken: http.RequestAborted));
        var dictRows = new List<Dictionary<string, string>>();
        foreach (var r in rows)
        {
            var dict = ((IDictionary<string, object?>)r).ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
            dictRows.Add(dict);
        }
        var bytes = ExcelTemplates.ExportToXlsx(type, dictRows);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"export_{type}.xlsx");
    }

    // ===== 文档上传 + 列表 + chunks + 删除 =====

    private static async Task<IResult> UploadDocument(
        [FromBody] UploadDocumentRequestDto req,
        IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ApiException(422, "missing_title", "title 必填");
        if (req.DocType is not ("manual" or "faq" or "manual_md" or "sop" or "other"))
            throw new ApiException(422, "invalid_doc_type", "doc_type 必须是 manual/faq/manual_md/sop/other");

        // 1) 插 document
        var docId = await repo.InsertDocumentAsync(
            req.Title, req.DocType, req.Brand, req.ModelScope,
            req.SourceFile, req.PageCount, req.CreatedBy, http.RequestAborted);

        // 2) 插 chunks + fire-and-forget 向量化
        var chunkIds = new List<long>();
        if (req.Chunks is not null)
        {
            foreach (var c in req.Chunks)
            {
                var chunkId = await repo.InsertChunkAsync(
                    docId, c.Level, c.Seq, c.Content, c.HeadingPath,
                    c.PageFrom, c.PageTo, "manual", req.CreatedBy, http.RequestAborted);
                chunkIds.Add(chunkId);

                // 单条 fire-and-forget 向量化（仅 level=2 子块参与检索；level=1 父块按设计不向量化）
                if (c.Level == 2)
                {
                    EnqueueChunkVectorize(chunkId, c.Content, repo, embedding, loggerFactory.CreateLogger("KnowledgeChunkVectorize"));
                }
            }
        }

        return Results.Ok(new UploadDocumentResponseDto
        {
            DocId = docId,
            ChunkIds = chunkIds,
        });
    }

    private static void EnqueueChunkVectorize(
        long chunkId, string content,
        IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding, ILogger log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await embedding.GenerateAsync(new[] { content }, cancellationToken: CancellationToken.None);
                if (result is null || result.Count == 0) return;
                var vec = result[0].Vector.ToArray();
                var ok = await repo.VectorizeChunkAsync(chunkId, vec, CancellationToken.None);
                log.LogInformation("[knowledge/chunk] 向量化完成 id={Id} ok={Ok}", chunkId, ok);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[knowledge/chunk] 向量化失败 id={Id}", chunkId);
            }
        });
    }

    private static async Task<IResult> ListDocuments(
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IKnowledgeEntryRepository repo,
        HttpContext http)
    {
        var lim = Math.Clamp(limit ?? 50, 1, 200);
        var off = Math.Max(offset ?? 0, 0);
        var items = await repo.ListDocumentsAsync(lim, off, http.RequestAborted);
        return Results.Ok(new DocumentListResponseDto
        {
            Items = items.Select(ToDocDto).ToList(),
            Limit = lim,
            Offset = off,
        });
    }

    private static async Task<IResult> ListChunks(
        long docId,
        IKnowledgeEntryRepository repo,
        HttpContext http)
    {
        var items = await repo.ListChunksAsync(docId, http.RequestAborted);
        return Results.Ok(new ChunkListResponseDto
        {
            Items = items.Select(ToChunkDto).ToList(),
        });
    }

    private static async Task<IResult> DeleteDocument(
        long docId,
        IKnowledgeEntryRepository repo,
        HttpContext http)
    {
        var ok = await repo.DeleteDocumentAsync(docId, http.RequestAborted);
        if (!ok) throw new ApiException(404, "document_not_found", $"doc_id={docId} 不存在");
        return Results.Ok(new DeleteDocumentResponseDto { DocId = docId, Deleted = true });
    }

    private static DocumentDto ToDocDto(DocumentListItem d) => new()
    {
        Id = d.Id,
        Title = d.Title,
        DocType = d.DocType,
        Brand = d.Brand,
        Status = d.Status,
        PageCount = d.PageCount,
        ErrorMsg = d.ErrorMsg,
        CreatedAt = d.CreatedAt,
    };

    private static ChunkDto ToChunkDto(ChunkListItem c) => new()
    {
        Id = c.Id,
        DocId = c.DocId,
        Level = c.Level,
        Seq = c.Seq,
        HeadingPath = c.HeadingPath,
        Content = c.Content,
        ContentLen = c.ContentLen,
        PageFrom = c.PageFrom,
        PageTo = c.PageTo,
        HasVector = c.HasVector,
    };

    // ===== POST /api/knowledge/entry =====

    private static async Task<IResult> CreateEntry(
        HttpContext http,
        IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory)
    {
        var (type, body) = await ReadTypeAndBodyAsync(http);
        if (type == "alarm")
        {
            var req = JsonSerializer.Deserialize<CreateAlarmRequestDto>(body, JsonOpts)
                ?? throw new ApiException(422, "invalid_body", "请求体反序列化失败");
            return await CreateAlarmAsync(req, repo, embedding, loggerFactory, http);
        }
        else if (type == "faq")
        {
            var req = JsonSerializer.Deserialize<CreateFaqRequestDto>(body, JsonOpts)
                ?? throw new ApiException(422, "invalid_body", "请求体反序列化失败");
            return await CreateFaqAsync(req, repo, embedding, loggerFactory, http);
        }
        throw new ApiException(422, "invalid_type", "type 必须为 alarm 或 faq");
    }

    private static async Task<(string Type, byte[] Body)> ReadTypeAndBodyAsync(HttpContext http)
    {
        http.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms);
        var body = ms.ToArray();
        http.Request.Body.Position = 0;
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("type", out var t))
            throw new ApiException(422, "missing_type", "请求体必须含 type 字段（alarm/faq）");
        var typeStr = t.GetString() ?? "";
        return (typeStr, body);
    }

    private static async Task<IResult> CreateAlarmAsync(
        CreateAlarmRequestDto req, IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(req.Brand) || string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            throw new ApiException(422, "missing_required", "brand/code/name 必填");

        long id;
        try
        {
            id = await repo.InsertAlarmAsync(new CreateAlarmRequest(
                req.Brand, req.Controller, req.Code, req.Name,
                req.Description, req.Cause, req.Action, req.SafetyNote,
                req.Category, req.Severity, req.CreatedBy), http.RequestAborted);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("alarm 已存在"))
        {
            throw new ApiException(409, "alarm_exists", ex.Message);
        }

        // 保存即触发后台向量化（fire-and-forget）
        EnqueueAlarmVectorize(id, repo, embedding, loggerFactory.CreateLogger("KnowledgeAlarmVectorize"));
        return Results.Ok(new EntryResponseDto { Type = "alarm", Id = id, Vectorized = true });
    }

    private static async Task<IResult> CreateFaqAsync(
        CreateFaqRequestDto req, IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            throw new ApiException(422, "missing_required", "title/body 必填");

        var (docId, chunkId) = await repo.InsertFaqAsync(
            req.Title, req.Body, req.Brand, req.ModelScope,
            req.Source, req.CreatedBy, http.RequestAborted);

        EnqueueFaqVectorize(docId, chunkId, $"{req.Title}\n{req.Body}".Trim(), repo, embedding, loggerFactory.CreateLogger("KnowledgeFaqVectorize"));
        return Results.Ok(new EntryResponseDto { Type = "faq", Id = docId, Vectorized = true });
    }

    // ===== PUT /api/knowledge/entry/{type}/{id} =====

    private static async Task<IResult> UpdateEntry(
        string type, long id,
        HttpContext http,
        IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory)
    {
        var (_, body) = await ReadTypeAndBodyAsync(http);

        if (type == "alarm")
        {
            var req = JsonSerializer.Deserialize<UpdateAlarmRequestDto>(body, JsonOpts)
                ?? throw new ApiException(422, "invalid_body", "请求体反序列化失败");
            var ok = await repo.UpdateAlarmAsync(id, new UpdateAlarmRequest(
                req.Name, req.Description, req.Cause, req.Action, req.SafetyNote, req.Category, req.Severity),
                http.RequestAborted);
            if (!ok) throw new ApiException(404, "alarm_not_found", $"alarm id={id} 不存在");
            EnqueueAlarmVectorize(id, repo, embedding, loggerFactory.CreateLogger("KnowledgeAlarmReVectorize"));
            return Results.Ok(new EntryResponseDto { Type = "alarm", Id = id, Vectorized = false });
        }
        else if (type == "faq")
        {
            var req = JsonSerializer.Deserialize<UpdateFaqRequestDto>(body, JsonOpts)
                ?? throw new ApiException(422, "invalid_body", "请求体反序列化失败");
            var newChunkId = await repo.UpdateFaqAsync(id, new UpdateFaqRequest(
                req.Title, req.Body, req.Brand, req.ModelScope), http.RequestAborted);
            if (newChunkId == 0) throw new ApiException(404, "faq_not_found", $"faq docId={id} 不存在");
            EnqueueFaqVectorize(id, newChunkId, $"{req.Title}\n{req.Body}".Trim(), repo, embedding, loggerFactory.CreateLogger("KnowledgeFaqReVectorize"));
            return Results.Ok(new EntryResponseDto { Type = "faq", Id = id, Vectorized = false });
        }
        throw new ApiException(422, "invalid_type", "type 必须为 alarm 或 faq");
    }

    // ===== DELETE /api/knowledge/entry/{type}/{id} =====

    private static async Task<IResult> DeleteEntry(
        string type, long id,
        IKnowledgeEntryRepository repo,
        ISuggestionRepository suggestions,
        ILoggerFactory loggerFactory,
        HttpContext http)
    {
        bool ok;
        if (type == "alarm") ok = await repo.DeleteAlarmAsync(id, http.RequestAborted);
        else if (type == "faq") ok = await repo.DeleteFaqAsync(id, http.RequestAborted);
        else throw new ApiException(422, "invalid_type", "type 必须为 alarm 或 faq");

        if (!ok) throw new ApiException(404, $"{type}_not_found", $"{type} id={id} 不存在");

        // 删除后反向重开引用该知识条的 resolved suggestion；重开失败不阻塞删除返回
        try
        {
            var reopened = await suggestions.ReopenByResolvedRefAsync(type, id, http.RequestAborted);
            if (reopened > 0)
                loggerFactory.CreateLogger("KnowledgeSuggestionReopen")
                    .LogInformation("[knowledge/delete] 重开 suggestion count={Count} 关联 type={Type} id={Id}",
                        reopened, type, id);
        }
        catch (Exception ex)
        {
            // 重开失败不应阻塞删除的成功响应
            loggerFactory.CreateLogger("KnowledgeSuggestionReopen")
                .LogError(ex, "[knowledge/delete] 重开 suggestion 失败 type={Type} id={Id}", type, id);
        }

        return Results.Ok(new DeleteEntryResponseDto { Type = type, Id = id, Deleted = true });
    }

    // ===== POST /api/knowledge/entry/{type}/{id}/re-vectorize =====

    private static async Task<IResult> ReVectorizeEntry(
        string type, long id,
        IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory,
        HttpContext http)
    {
        if (type == "alarm")
        {
            var ok = await ReVectorizeAlarmAsync(id, repo, embedding, loggerFactory.CreateLogger("KnowledgeAlarmReVectorize"));
            return Results.Ok(new ReVectorizeResponseDto { Type = "alarm", Id = id, Vectorized = ok });
        }
        else if (type == "faq")
        {
            var target = await repo.GetFaqVectorizeTargetAsync(id, http.RequestAborted);
            if (target is null) throw new ApiException(404, "faq_not_found", $"faq docId={id} 不存在");
            var ok = await ReVectorizeFaqAsync(target.Value.PrimaryChunkId, target.Value.Text, repo, embedding, loggerFactory.CreateLogger("KnowledgeFaqReVectorize"));
            return Results.Ok(new ReVectorizeResponseDto { Type = "faq", Id = id, Vectorized = ok });
        }
        throw new ApiException(422, "invalid_type", "type 必须为 alarm 或 faq");
    }

    private static void EnqueueAlarmVectorize(
        long id, IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding, ILogger log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var text = await repo.GetAlarmVectorizeTextAsync(id, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(text)) return;
                var result = await embedding.GenerateAsync(new[] { text }, cancellationToken: CancellationToken.None);
                if (result is null || result.Count == 0) return;
                var vec = result[0].Vector.ToArray();
                var ok = await repo.VectorizeAlarmAsync(id, vec, CancellationToken.None);
                log.LogInformation("[knowledge/alarm] 向量化完成 id={Id} ok={Ok}", id, ok);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[knowledge/alarm] 向量化失败 id={Id}", id);
            }
        });
    }

    private static void EnqueueFaqVectorize(
        long docId, long chunkId, string text,
        IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding, ILogger log)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await embedding.GenerateAsync(new[] { text }, cancellationToken: CancellationToken.None);
                if (result is null || result.Count == 0) return;
                var vec = result[0].Vector.ToArray();
                var ok = await repo.VectorizeChunkAsync(chunkId, vec, CancellationToken.None);
                log.LogInformation("[knowledge/faq] 向量化完成 docId={DocId} chunkId={ChunkId} ok={Ok}", docId, chunkId, ok);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[knowledge/faq] 向量化失败 docId={DocId}", docId);
            }
        });
    }

    private static async Task<bool> ReVectorizeAlarmAsync(
        long id, IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding, ILogger log)
    {
        try
        {
            var text = await repo.GetAlarmVectorizeTextAsync(id, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text)) return false;
            var result = await embedding.GenerateAsync(new[] { text }, cancellationToken: CancellationToken.None);
            if (result is null || result.Count == 0) return false;
            var vec = result[0].Vector.ToArray();
            return await repo.VectorizeAlarmAsync(id, vec, CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[knowledge/alarm] 重向量化失败 id={Id}", id);
            return false;
        }
    }

    private static async Task<bool> ReVectorizeFaqAsync(
        long chunkId, string text, IKnowledgeEntryRepository repo,
        IEmbeddingGenerator<string, Embedding<float>> embedding, ILogger log)
    {
        try
        {
            var result = await embedding.GenerateAsync(new[] { text }, cancellationToken: CancellationToken.None);
            if (result is null || result.Count == 0) return false;
            var vec = result[0].Vector.ToArray();
            return await repo.VectorizeChunkAsync(chunkId, vec, CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[knowledge/faq] 重向量化失败 chunkId={ChunkId}", chunkId);
            return false;
        }
    }

    // ===== GET /api/knowledge/entries =====

    private static async Task<IResult> ListEntries(
        [FromQuery] string? type,
        [FromQuery] string? origin,
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IKnowledgeEntryRepository repo,
        HttpContext http)
    {
        if (type is not null && type is not ("alarm" or "faq"))
            throw new ApiException(422, "invalid_type", "type 必须为 alarm 或 faq");
        if (origin is not null && origin is not ("ingest" or "manual" or "feedback"))
            throw new ApiException(422, "invalid_origin", "origin 必须为 ingest/manual/feedback");

        var lim = Math.Clamp(limit ?? 50, 1, 200);
        var off = Math.Max(offset ?? 0, 0);
        var query = new KnowledgeListQuery(Type: type, Origin: origin, Q: q, Limit: lim, Offset: off);
        var (items, total) = await repo.ListEntriesAsync(query, http.RequestAborted);
        return Results.Ok(new KnowledgeListResponseDto
        {
            Items = items.Select(ToListItemDto).ToList(),
            Total = total,
            Limit = lim,
            Offset = off,
        });
    }

    private static KnowledgeEntryListItemDto ToListItemDto(KnowledgeEntryListItem r) => new()
    {
        Type = r.Type,
        Id = r.Id,
        DocId = r.DocId,
        Title = r.Title,
        Origin = r.Origin,
        CreatedBy = r.CreatedBy,
        CreatedAt = r.CreatedAt,
        Vectorized = r.Vectorized,
    };
}
