// Api/Contracts/StatsDtos.cs —— 高频故障 Top-N 看板
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class TopFaultItemDto
{
    [JsonPropertyName("code_norm")] public string CodeNorm { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class TopFaultsWindowDto
{
    [JsonPropertyName("from_time")] public DateTimeOffset? FromTime { get; set; }
    [JsonPropertyName("to_time")] public DateTimeOffset ToTime { get; set; }
    [JsonPropertyName("days")] public int? Days { get; set; }
}

public sealed class TopFaultsResponseDto
{
    [JsonPropertyName("window")] public TopFaultsWindowDto Window { get; set; } = new();
    [JsonPropertyName("total_query_logs")] public long TotalQueryLogs { get; set; }
    [JsonPropertyName("total_maintenance_logs")] public long TotalMaintenanceLogs { get; set; }
    [JsonPropertyName("by_query")] public List<TopFaultItemDto> ByQuery { get; set; } = new();
    [JsonPropertyName("by_maintenance")] public List<TopFaultItemDto> ByMaintenance { get; set; } = new();
}
