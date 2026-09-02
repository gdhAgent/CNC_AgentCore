// Api/Endpoints/VectorEndpoints.cs —— /api/vectors：overview、unvectorized、vectorize/{table}（后台补跑）、embedding-map（服务端 PCA 2D）
// 组级 RequireAuth。
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Application.Vectors;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CNC_AgentCore.Api.Endpoints;

public static class VectorEndpoints
{
    private static readonly HashSet<string> ValidTables = new(StringComparer.Ordinal)
    {
        "alarms", "chunks", "maintenance_logs",
    };

    // 后台 fire-and-forget 任务强引用：防 Task.Run 后无引用被 GC 提前回收
    private static readonly HashSet<Task> _backgroundTasks = new();

    public static IEndpointRouteBuilder MapVectorEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/vectors").WithTags("vectors")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapGet("/overview", Overview).WithName("VectorsOverview");
        g.MapGet("/unvectorized", Unvectorized).WithName("VectorsUnvectorized");
        g.MapPost("/vectorize/{table}", VectorizeTrigger).WithName("VectorsVectorize");
        g.MapGet("/embedding-map", EmbeddingMap).WithName("VectorsEmbeddingMap");
        return app;
    }

    private static async Task<IResult> Overview(IVectorRepository vectors, HttpContext http)
    {
        var stats = await vectors.GetOverviewAsync(http.RequestAborted);
        return Results.Ok(new OverviewResponseDto
        {
            Tables = stats.Select(ToDto).ToList(),
        });
    }

    // ===== GET /api/vectors/unvectorized =====

    private static async Task<IResult> Unvectorized(
        [FromQuery] string table,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IVectorRepository vectors,
        HttpContext http)
    {
        if (string.IsNullOrEmpty(table) || !ValidTables.Contains(table))
            throw new ApiException(422, "invalid_table",
                $"table 必须为 {string.Join('/', ValidTables)} 之一");

        var lim = Math.Clamp(limit ?? 50, 1, 200);
        var off = Math.Max(offset ?? 0, 0);

        var (items, total) = await vectors.ListUnvectorizedAsync(table, lim, off, http.RequestAborted);
        return Results.Ok(new UnvectorizedResponseDto
        {
            Table = table,
            Total = total,
            Items = items.Select(ToItemDto).ToList(),
            Limit = lim,
            Offset = off,
        });
    }

    // ===== POST /api/vectors/vectorize/{table}（fire-and-forget 后台补跑）=====

    private static IResult VectorizeTrigger(
        string table,
        IServiceScopeFactory scopes,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrEmpty(table) || !ValidTables.Contains(table))
            throw new ApiException(422, "invalid_table",
                $"table 必须为 {string.Join('/', ValidTables)} 之一");

        var log = loggerFactory.CreateLogger("VectorizeBackground");
        var task = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IVectorizeService>();
                var result = await svc.RunAsync(table, batch: 10, CancellationToken.None);
                log.LogInformation("[vectors] 补跑完成 table={Table} embedded={Embedded} failed={Failed}",
                    result.Table, result.Embedded, result.Failed);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[vectors] 补跑失败 table={Table}", table);
            }
        });
        _backgroundTasks.Add(task);
        // 完成时从集合移除（防内存泄漏）
        task.ContinueWith(t => _backgroundTasks.Remove(t), TaskScheduler.Default);

        return Results.Ok(new VectorizeResponseDto
        {
            Table = table,
            Started = true,
            Note = "后台补跑中，可稍后刷新总览查看进度",
        });
    }

    private static OverviewTableDto ToDto(VectorTableStat s) => new()
    {
        Table = s.Table,
        Label = s.Label,
        Note = s.Note,
        DesignedSkip = s.DesignedSkip,
        Total = s.Total,
        WithEmbedding = s.WithEmbedding,
        Without = s.Without,
        DimMin = s.DimMin,
        DimMax = s.DimMax,
    };

    private static UnvectorizedItemDto ToItemDto(UnvectorizedItem r) => new()
    {
        Id = r.Id,
        Code = r.Code,
        Level = r.Level,
        Title = r.Title,
        Detail = r.Detail,
    };

    // ===== GET /api/vectors/embedding-map（服务端 PCA 2D，输出 x/y）=====

    private static async Task<IResult> EmbeddingMap(
        [FromQuery] string table,
        [FromQuery(Name = "group_by")] string? groupBy,
        IVectorRepository vectors,
        HttpContext http)
    {
        if (string.IsNullOrEmpty(table) || !ValidTables.Contains(table))
            throw new ApiException(422, "invalid_table",
                $"table 必须为 {string.Join('/', ValidTables)} 之一");

        // 缺省 group_by → 表默认第一个；显式值必须在校验白名单内
        var resolvedGroupBy = groupBy ?? VectorRepository.GroupByDefault[table];
        if (!VectorRepository.GroupByOptions[table].Contains(resolvedGroupBy))
            throw new ApiException(422, "invalid_group_by",
                $"table={table} 的 group_by 必须为 {string.Join('/', VectorRepository.GroupByOptions[table])} 之一");

        // 固定 limit=200（PCA 与响应体积够用）
        var items = await vectors.FetchEmbeddingMapAsync(table, resolvedGroupBy, limit: 200, http.RequestAborted);

        // PCA：中心化 → 前 2 主成分 + 解释方差
        var pca = VectorPca.Project2D(items.Select(i => i.Vec).ToList());
        var dtoItems = new List<EmbeddingMapItemDto>(items.Count);
        for (var i = 0; i < items.Count; i++)
            dtoItems.Add(ToEmbeddingDto(items[i], pca.Xs[i], pca.Ys[i]));

        return Results.Ok(new EmbeddingMapResponseDto
        {
            Table = table,
            GroupBy = resolvedGroupBy,
            Count = items.Count,
            ExplainedVariance = new[] { pca.Explained0, pca.Explained1 },
            Items = dtoItems,
        });
    }

    private static EmbeddingMapItemDto ToEmbeddingDto(EmbeddingMapItem r, double x, double y) => new()
    {
        Id = r.Id,
        X = x,
        Y = y,
        Label = r.Label,
        Group = r.Group,
    };
}
