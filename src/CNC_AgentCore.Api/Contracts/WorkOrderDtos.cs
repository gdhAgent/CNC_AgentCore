// Api/Contracts/WorkOrderDtos.cs —— 工单端点契约（#14）。
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class CreateWorkorderRequestDto
{
    [JsonPropertyName("machine_id")] public long MachineId { get; set; }
    [JsonPropertyName("order_no")] public string? OrderNo { get; set; }
    [JsonPropertyName("alarm_code")] public string? AlarmCode { get; set; }
    [JsonPropertyName("fault_type")] public string? FaultType { get; set; }
    [JsonPropertyName("symptom")] public string Symptom { get; set; } = "";
    [JsonPropertyName("root_cause")] public string? RootCause { get; set; }
    [JsonPropertyName("action_taken")] public string? ActionTaken { get; set; }
    [JsonPropertyName("parts_used")] public List<Dictionary<string, object?>>? PartsUsed { get; set; }
    [JsonPropertyName("engineer")] public string? Engineer { get; set; }
    [JsonPropertyName("downtime_min")] public int? DowntimeMin { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("is_demo")] public bool IsDemo { get; set; }
}

public sealed class CreateWorkorderResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("machine_id")] public long MachineId { get; set; }
    [JsonPropertyName("vectorizing")] public bool Vectorizing { get; set; }
    [JsonPropertyName("sync")] public bool Sync { get; set; }
}

/// <summary>设备台账单条（#15 GET /api/workorders/machines）。</summary>
public sealed class MachineDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("asset_no")] public string AssetNo { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("brand")] public string Brand { get; set; } = "";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("workshop")] public string? Workshop { get; set; }
    [JsonPropertyName("line_no")] public string? LineNo { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "running";
    [JsonPropertyName("is_demo")] public bool IsDemo { get; set; }
    [JsonPropertyName("workorder_count")] public long WorkorderCount { get; set; }
}

public sealed class MachinesListResponseDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("items")] public List<MachineDto> Items { get; set; } = new();
}

/// <summary>工单列表单条（#15 GET /api/workorders）。</summary>
public sealed class WorkorderListItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("order_no")] public string? OrderNo { get; set; }
    [JsonPropertyName("machine_id")] public long MachineId { get; set; }
    [JsonPropertyName("asset_no")] public string? AssetNo { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("alarm_code")] public string? AlarmCode { get; set; }
    [JsonPropertyName("fault_type")] public string? FaultType { get; set; }
    [JsonPropertyName("symptom")] public string? Symptom { get; set; }
    [JsonPropertyName("root_cause")] public string? RootCause { get; set; }
    [JsonPropertyName("action_taken")] public string? ActionTaken { get; set; }
    [JsonPropertyName("engineer")] public string? Engineer { get; set; }
    [JsonPropertyName("downtime_min")] public int? DowntimeMin { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("is_demo")] public bool IsDemo { get; set; }
    [JsonPropertyName("alarm_name")] public string? AlarmName { get; set; }
    [JsonPropertyName("alarm_severity")] public string? AlarmSeverity { get; set; }
}

public sealed class WorkordersListResponseDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("items")] public List<WorkorderListItemDto> Items { get; set; } = new();
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

/// <summary>工单详情（#16）。含 parts_used + alarm_cause（列表没有的字段）。</summary>
public sealed class WorkorderDetailDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("order_no")] public string? OrderNo { get; set; }
    [JsonPropertyName("machine_id")] public long MachineId { get; set; }
    [JsonPropertyName("asset_no")] public string? AssetNo { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("alarm_code")] public string? AlarmCode { get; set; }
    [JsonPropertyName("fault_type")] public string? FaultType { get; set; }
    [JsonPropertyName("symptom")] public string? Symptom { get; set; }
    [JsonPropertyName("root_cause")] public string? RootCause { get; set; }
    [JsonPropertyName("action_taken")] public string? ActionTaken { get; set; }
    [JsonPropertyName("parts_used")] public List<Dictionary<string, object?>> PartsUsed { get; set; } = new();
    [JsonPropertyName("engineer")] public string? Engineer { get; set; }
    [JsonPropertyName("downtime_min")] public int? DowntimeMin { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("is_demo")] public bool IsDemo { get; set; }
    [JsonPropertyName("alarm_name")] public string? AlarmName { get; set; }
    [JsonPropertyName("alarm_severity")] public string? AlarmSeverity { get; set; }
    [JsonPropertyName("alarm_cause")] public string? AlarmCause { get; set; }
}

public sealed class DeleteWorkorderResponseDto
{
    [JsonPropertyName("deleted")] public long Deleted { get; set; }
}
