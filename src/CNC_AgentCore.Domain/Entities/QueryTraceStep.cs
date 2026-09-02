// Domain/Entities/QueryTraceStep.cs —— log.query_trace_steps
namespace CNC_AgentCore.Domain.Entities;

public sealed class QueryTraceStep
{
    public long Id { get; set; }
    public Guid TraceId { get; set; }
    public int Seq { get; set; }
    public string Step { get; set; } = string.Empty;    // 见 TraceRecorder.VALID_STEPS
    public string Status { get; set; } = "ok";         // ok/skipped/failed/timeout
    public int Ms { get; set; }
    public string? Input { get; set; }                  // jsonb
    public string? Output { get; set; }                 // jsonb
    public string? Note { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
