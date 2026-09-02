// Api/Contracts/TraceDtos.cs —— 检索排查数据
// #8 TraceStepItemDto + RankingRowDto + TraceResponseDto
// #9 LogItemDto + LogListResponseDto
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class TraceStepItemDto
{
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("step")] public string Step { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "ok";
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("ms")] public int Ms { get; set; }
    [JsonPropertyName("input")] public Dictionary<string, object?> Input { get; set; } = new();
    [JsonPropertyName("output")] public Dictionary<string, object?> Output { get; set; } = new();
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public sealed class RankingRowDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("vector_rank")] public int? VectorRank { get; set; }
    [JsonPropertyName("fulltext_rank")] public int? FulltextRank { get; set; }
    [JsonPropertyName("rrf_rank")] public int? RrfRank { get; set; }
    [JsonPropertyName("rerank_rank")] public int? RerankRank { get; set; }
    [JsonPropertyName("final")] public bool Final { get; set; }
}

public sealed class TraceResponseDto
{
    [JsonPropertyName("trace_id")] public Guid TraceId { get; set; }
    [JsonPropertyName("question")] public string Question { get; set; } = "";
    [JsonPropertyName("route")] public string Route { get; set; } = "";
    [JsonPropertyName("refused")] public bool Refused { get; set; }
    [JsonPropertyName("detected_codes")] public List<string> DetectedCodes { get; set; } = new();
    [JsonPropertyName("answer")] public string? Answer { get; set; }
    [JsonPropertyName("latency_ms")] public int? LatencyMs { get; set; }
    [JsonPropertyName("latency_breakdown")] public Dictionary<string, int> LatencyBreakdown { get; set; } = new();
    [JsonPropertyName("tool_calls")] public List<Dictionary<string, object?>> ToolCalls { get; set; } = new();
    [JsonPropertyName("feedback")] public int? Feedback { get; set; }            // 1=赞 -1=踩 NULL=未评价
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("steps")] public List<TraceStepItemDto> Steps { get; set; } = new();
    [JsonPropertyName("ranking_comparison")] public List<RankingRowDto> RankingComparison { get; set; } = new();
}

/// <summary>日志列表单条（#9）。</summary>
public sealed class LogItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("trace_id")] public Guid TraceId { get; set; }
    [JsonPropertyName("raw_query")] public string RawQuery { get; set; } = "";
    [JsonPropertyName("route")] public string Route { get; set; } = "";
    [JsonPropertyName("refused")] public bool Refused { get; set; }
    [JsonPropertyName("feedback")] public int? Feedback { get; set; }              // 1=赞 -1=踩 NULL=未评价
    [JsonPropertyName("latency_ms")] public int? LatencyMs { get; set; }
    [JsonPropertyName("user_code")] public string? UserCode { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class LogListResponseDto
{
    [JsonPropertyName("items")] public List<LogItemDto> Items { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}
