// Api/Contracts/QueryDtos.cs —— /api/cnc/query 请求 / 响应
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class QueryRequest
{
    [JsonPropertyName("query")] public string Query { get; set; } = "";
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("user_code")] public string? UserCode { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("machine_model")] public string? MachineModel { get; set; }
    [JsonPropertyName("top_n")] public int TopN { get; set; } = 5;
}

public sealed class TopKItem
{
    [JsonPropertyName("ref")] public int Ref { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("channel")] public List<string> Channel { get; set; } = new();
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("code_norm")] public string? CodeNorm { get; set; }
}

public sealed class TimingInfo
{
    [JsonPropertyName("embed")] public int Embed { get; set; }
    [JsonPropertyName("code_extract")] public int CodeExtract { get; set; }
    [JsonPropertyName("exact_match")] public int ExactMatch { get; set; }
    [JsonPropertyName("vector_recall")] public int VectorRecall { get; set; }
    [JsonPropertyName("fulltext_recall")] public int FulltextRecall { get; set; }
    [JsonPropertyName("rrf_fusion")] public int RrfFusion { get; set; }
    [JsonPropertyName("rerank")] public int Rerank { get; set; }
    [JsonPropertyName("threshold_gate")] public int ThresholdGate { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

public sealed class QueryResponse
{
    [JsonPropertyName("trace_id")] public string TraceId { get; set; } = "";
    [JsonPropertyName("route")] public string Route { get; set; } = "";
    [JsonPropertyName("detected_codes")] public List<string> DetectedCodes { get; set; } = new();
    [JsonPropertyName("refused")] public bool Refused { get; set; }
    [JsonPropertyName("refused_reason")] public string? RefusedReason { get; set; }
    [JsonPropertyName("topk")] public List<TopKItem> Topk { get; set; } = new();
    [JsonPropertyName("suggest_hits")] public List<TopKItem> SuggestHits { get; set; } = new();
    [JsonPropertyName("tool_calls")] public List<Dictionary<string, object?>> ToolCalls { get; set; } = new();
    [JsonPropertyName("timing")] public TimingInfo Timing { get; set; } = new();
}
