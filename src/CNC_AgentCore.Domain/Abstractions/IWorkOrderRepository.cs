// Domain/Abstractions/IWorkOrderRepository.cs —— 工单数据访问（#14/#15/#16）。
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>新增工单字段（#14）。</summary>
public sealed record CreateWorkorderRequest(
    long MachineId,
    string? OrderNo,
    string? AlarmCode,
    string? FaultType,
    string Symptom,
    string? RootCause,
    string? ActionTaken,
    List<Dictionary<string, object?>>? PartsUsed,
    string? Engineer,
    int? DowntimeMin,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    bool IsDemo);

/// <summary>单条工单 + 设备 + 报警码上下文，用于向量化（#14 + 与 #12 maintenance_logs 同构）。</summary>
public sealed record MaintenanceLogVectorizeRow(
    long Id,
    string? AlarmCode,
    string? FaultType,
    string? Symptom,
    string? ActionTaken,
    string AssetNo,
    string? Brand,
    string? Model,
    string? Controller);

/// <summary>设备台账列表单条（#15），与 ops.machines 字段对齐。</summary>
public sealed record MachineListItem(
    long Id,
    string AssetNo,
    string Name,
    string Brand,
    string? Model,
    string? Controller,
    string? Workshop,
    string? LineNo,
    string Status,
    bool IsDemo,
    long WorkorderCount);

/// <summary>工单列表查询条件（#15）。</summary>
public sealed record WorkorderListQuery(
    string? AlarmCode = null,
    long? MachineId = null,
    string? Brand = null,
    string? FaultType = null,
    DateTimeOffset? FromTime = null,
    DateTimeOffset? ToTime = null,
    int Limit = 50,
    int Offset = 0);

/// <summary>工单列表单条（#15）。JOIN machines 取 brand/model/asset_no + JOIN kb.alarms 取 name/severity。</summary>
public sealed record WorkorderListItem(
    long Id,
    string? OrderNo,
    long MachineId,
    string? AssetNo,
    string? Brand,
    string? Model,
    string? AlarmCode,
    string? FaultType,
    string? Symptom,
    string? RootCause,
    string? ActionTaken,
    string? Engineer,
    int? DowntimeMin,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    bool IsDemo,
    string? AlarmName,
    string? AlarmSeverity);

/// <summary>工单详情（#16 GET /api/workorders/{id}）：列表字段 + parts_used + alarm_cause。</summary>
public sealed record WorkorderDetail(
    long Id,
    string? OrderNo,
    long MachineId,
    string? AssetNo,
    string? Brand,
    string? Model,
    string? AlarmCode,
    string? FaultType,
    string? Symptom,
    string? RootCause,
    string? ActionTaken,
    List<Dictionary<string, object?>> PartsUsed,
    string? Engineer,
    int? DowntimeMin,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    bool IsDemo,
    string? AlarmName,
    string? AlarmSeverity,
    string? AlarmCause);

public interface IWorkOrderRepository
{
    /// <summary>校验 machine_id 存在（#14 POST 校验前置）。</summary>
    Task<bool> MachineExistsAsync(long machineId, CancellationToken ct = default);

    /// <summary>插入一条 ops.maintenance_logs，返回新 id（#14）。</summary>
    Task<long> InsertAsync(CreateWorkorderRequest req, CancellationToken ct = default);

    /// <summary>取单条工单的向量化输入行（带设备上下文），供 embed 文本构造（#14 后台 fire-and-forget）。</summary>
    Task<MaintenanceLogVectorizeRow?> FetchVectorizeRowAsync(long workorderId, CancellationToken ct = default);

    /// <summary>写回单条工单的 embedding（#14 后台 fire-and-forget 第二步）。</summary>
    Task<bool> UpdateEmbeddingAsync(long workorderId, float[] vector, CancellationToken ct = default);

    /// <summary>设备台账列表（#15 GET /api/workorders/machines）。</summary>
    Task<List<MachineListItem>> ListMachinesAsync(int limit, int offset, CancellationToken ct = default);

    /// <summary>工单列表（#15 GET /api/workorders）：筛选 + 分页 + total。</summary>
    Task<(List<WorkorderListItem> Items, int Total)> ListWorkordersAsync(
        WorkorderListQuery query, CancellationToken ct = default);

    /// <summary>工单详情（#16 GET /api/workorders/{id}）；不存在返回 null。</summary>
    Task<WorkorderDetail?> GetDetailAsync(long workorderId, CancellationToken ct = default);

    /// <summary>删除工单（#16 DELETE /api/workorders/{id}）；不存在返回 false。</summary>
    Task<bool> DeleteAsync(long workorderId, CancellationToken ct = default);
}
