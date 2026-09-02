// Domain/Entities/QueryLog.cs —— log.query_logs
namespace CNC_AgentCore.Domain.Entities;

public sealed class QueryLog
{
    public Guid TraceId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string? Route { get; set; }                  // agent/rag_fallback/refused
    public bool? Refused { get; set; }
    public string? RefusedReason { get; set; }
    public string? Answer { get; set; }
    public int? Rounds { get; set; }
    public bool? Degraded { get; set; }
    public int? TotalMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? Username { get; set; }               // V1.5 鉴权后写入
    public string? Ip { get; set; }
    public string? Retrieved { get; set; }              // jsonb
    public string Status { get; set; } = "running";     // running/done/error
    public DateTimeOffset CreatedAt { get; set; }
}
