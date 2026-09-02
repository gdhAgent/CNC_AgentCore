// Api/Endpoints/WorkOrderEndpoints.cs —— /api/workorders：创建、machines 列表、列表、详情、删除（组级 RequireAuth）
// 创建后即后台 fire-and-forget 向量化，响应恒 Vectorizing=true（无同步参数）。
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Mvc;

namespace CNC_AgentCore.Api.Endpoints;

public static class WorkOrderEndpoints
{
    // 后台 fire-and-forget 任务强引用（避免 Task.Run 后无引用被 GC 提前回收）
    private static readonly HashSet<Task> _backgroundTasks = new();

    public static IEndpointRouteBuilder MapWorkOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/workorders").WithTags("workorders")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapPost("", CreateWorkorder).WithName("WorkorderCreate");
        g.MapGet("/machines", ListMachines).WithName("WorkorderListMachines");
        g.MapGet("", ListWorkorders).WithName("WorkorderList");
        g.MapGet("/{id:long}", GetDetail).WithName("WorkorderDetail");
        g.MapDelete("/{id:long}", DeleteWorkorder).WithName("WorkorderDelete");
        return app;
    }

    private static async Task<IResult> CreateWorkorder(
        [FromBody] CreateWorkorderRequestDto body,
        IWorkOrderRepository workOrders,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory,
        HttpContext http)
    {
        // —— 1) 入参校验 ——
        if (body is null || string.IsNullOrWhiteSpace(body.Symptom))
            throw new ApiException(422, "invalid_symptom", "symptom 不能为空");
        if (body.MachineId <= 0)
            throw new ApiException(422, "invalid_machine_id", "machine_id 必须 > 0");

        // —— 2) machine_id 存在性 ——
        if (!await workOrders.MachineExistsAsync(body.MachineId, http.RequestAborted))
            throw new ApiException(404, "machine_not_found", $"machine id={body.MachineId} 不存在");

        // —— 3) INSERT ——
        var req = new CreateWorkorderRequest(
            MachineId: body.MachineId,
            OrderNo: body.OrderNo,
            AlarmCode: body.AlarmCode,
            FaultType: body.FaultType,
            Symptom: body.Symptom,
            RootCause: body.RootCause,
            ActionTaken: body.ActionTaken,
            PartsUsed: body.PartsUsed,
            Engineer: body.Engineer,
            DowntimeMin: body.DowntimeMin,
            StartedAt: body.StartedAt,
            FinishedAt: body.FinishedAt,
            IsDemo: body.IsDemo);
        var id = await workOrders.InsertAsync(req, http.RequestAborted);

        // —— 4) 立即返回 + 后台 fire-and-forget 向量化（_backgroundTasks 持强引用防 GC；依赖均 Singleton，可安全跨 Task）——
        EnqueueVectorize(id, workOrders, embedding, loggerFactory);
        return Results.Ok(new CreateWorkorderResponseDto
        {
            Id = id,
            MachineId = body.MachineId,
            Vectorizing = true,
            Sync = false,
        });
    }

    /// <summary>
    /// 触发单条工单后台向量化（fire-and-forget）。由外部在 endpoint 内创建 Task 调用。
    /// </summary>
    private static void EnqueueVectorize(
        long workorderId,
        IWorkOrderRepository workOrders,
        IEmbeddingGenerator<string, Embedding<float>> embedding,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("WorkorderVectorizeBackground");
        var task = Task.Run(async () =>
        {
            try
            {
                var row = await workOrders.FetchVectorizeRowAsync(workorderId, CancellationToken.None);
                if (row is null)
                {
                    log.LogWarning("[workorder] 向量化跳过：row 不存在 id={Id}", workorderId);
                    return;
                }
                var text = BuildMaintenanceLogText(row);
                if (string.IsNullOrWhiteSpace(text))
                {
                    log.LogWarning("[workorder] 向量化跳过：空文本 id={Id}", workorderId);
                    return;
                }
                var result = await embedding.GenerateAsync(new[] { text }, cancellationToken: CancellationToken.None);
                if (result is null || result.Count == 0) return;
                var vec = result[0].Vector.ToArray();
                var ok = await workOrders.UpdateEmbeddingAsync(workorderId, vec, CancellationToken.None);
                log.LogInformation("[workorder] 向量化完成 id={Id} ok={Ok}", workorderId, ok);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[workorder] 向量化失败 id={Id}", workorderId);
            }
        });
        _backgroundTasks.Add(task);
        task.ContinueWith(t => _backgroundTasks.Remove(t), TaskScheduler.Default);
    }

    /// <summary>由工单行拼出向量化用文本。</summary>
    private static string BuildMaintenanceLogText(MaintenanceLogVectorizeRow r)
    {
        var parts = new List<string> { $"[{r.AssetNo ?? "?"}][{r.AlarmCode ?? "无"}]" };
        if (!string.IsNullOrEmpty(r.FaultType)) parts.Add($"[{r.FaultType}]");
        parts.Add((r.Symptom ?? "").Trim());
        if (!string.IsNullOrEmpty(r.ActionTaken)) parts.Add($"处置：{r.ActionTaken.Trim()}");
        return string.Join("\n", parts);
    }

    // ===== GET /api/workorders/machines + GET /api/workorders =====

    private static async Task<IResult> ListMachines(
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IWorkOrderRepository workOrders,
        HttpContext http)
    {
        var lim = Math.Clamp(limit ?? 100, 1, 500);
        var off = Math.Max(offset ?? 0, 0);
        var items = await workOrders.ListMachinesAsync(lim, off, http.RequestAborted);
        return Results.Ok(new MachinesListResponseDto
        {
            Total = items.Count,
            Items = items.Select(ToMachineDto).ToList(),
        });
    }

    private static async Task<IResult> ListWorkorders(
        [FromQuery] string? alarm_code,
        [FromQuery] long? machine_id,
        [FromQuery] string? brand,
        [FromQuery] string? fault_type,
        [FromQuery(Name = "from_time")] DateTimeOffset? fromTime,
        [FromQuery(Name = "to_time")] DateTimeOffset? toTime,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        IWorkOrderRepository workOrders,
        HttpContext http)
    {
        var lim = Math.Clamp(limit ?? 50, 1, 200);
        var off = Math.Max(offset ?? 0, 0);
        var query = new WorkorderListQuery(
            AlarmCode: alarm_code,
            MachineId: machine_id,
            Brand: brand,
            FaultType: fault_type,
            FromTime: fromTime,
            ToTime: toTime,
            Limit: lim,
            Offset: off);
        var (items, total) = await workOrders.ListWorkordersAsync(query, http.RequestAborted);
        return Results.Ok(new WorkordersListResponseDto
        {
            Total = total,
            Items = items.Select(ToWorkorderListDto).ToList(),
            Limit = lim,
            Offset = off,
        });
    }

    private static MachineDto ToMachineDto(MachineListItem m) => new()
    {
        Id = m.Id,
        AssetNo = m.AssetNo,
        Name = m.Name,
        Brand = m.Brand,
        Model = m.Model,
        Controller = m.Controller,
        Workshop = m.Workshop,
        LineNo = m.LineNo,
        Status = m.Status,
        IsDemo = m.IsDemo,
        WorkorderCount = m.WorkorderCount,
    };

    private static WorkorderListItemDto ToWorkorderListDto(WorkorderListItem w) => new()
    {
        Id = w.Id,
        OrderNo = w.OrderNo,
        MachineId = w.MachineId,
        AssetNo = w.AssetNo,
        Brand = w.Brand,
        Model = w.Model,
        AlarmCode = w.AlarmCode,
        FaultType = w.FaultType,
        Symptom = w.Symptom,
        RootCause = w.RootCause,
        ActionTaken = w.ActionTaken,
        Engineer = w.Engineer,
        DowntimeMin = w.DowntimeMin,
        StartedAt = w.StartedAt,
        FinishedAt = w.FinishedAt,
        IsDemo = w.IsDemo,
        AlarmName = w.AlarmName,
        AlarmSeverity = w.AlarmSeverity,
    };

    // ===== GET /api/workorders/{id} + DELETE =====

    private static async Task<IResult> GetDetail(
        long id,
        IWorkOrderRepository workOrders,
        HttpContext http)
    {
        var detail = await workOrders.GetDetailAsync(id, http.RequestAborted);
        if (detail is null)
            throw new ApiException(404, "workorder_not_found", $"workorder id={id} 不存在");
        return Results.Ok(ToDetailDto(detail));
    }

    private static async Task<IResult> DeleteWorkorder(
        long id,
        IWorkOrderRepository workOrders,
        HttpContext http)
    {
        var ok = await workOrders.DeleteAsync(id, http.RequestAborted);
        if (!ok)
            throw new ApiException(404, "workorder_not_found", $"workorder id={id} 不存在");
        return Results.Ok(new DeleteWorkorderResponseDto { Deleted = id });
    }

    private static WorkorderDetailDto ToDetailDto(WorkorderDetail d) => new()
    {
        Id = d.Id,
        OrderNo = d.OrderNo,
        MachineId = d.MachineId,
        AssetNo = d.AssetNo,
        Brand = d.Brand,
        Model = d.Model,
        AlarmCode = d.AlarmCode,
        FaultType = d.FaultType,
        Symptom = d.Symptom,
        RootCause = d.RootCause,
        ActionTaken = d.ActionTaken,
        PartsUsed = d.PartsUsed,
        Engineer = d.Engineer,
        DowntimeMin = d.DowntimeMin,
        StartedAt = d.StartedAt,
        FinishedAt = d.FinishedAt,
        IsDemo = d.IsDemo,
        AlarmName = d.AlarmName,
        AlarmSeverity = d.AlarmSeverity,
        AlarmCause = d.AlarmCause,
    };
}
