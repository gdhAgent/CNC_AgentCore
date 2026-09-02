// Domain/Entities/EvalItem.cs —— log.eval_items（评估集条目）
namespace CNC_AgentCore.Domain.Entities;

public sealed class EvalItem
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public string? ExpectedDocIds { get; set; }           // jsonb 数组
    public string? ExpectedAlarmCodes { get; set; }       // jsonb 数组
    public string? Tags { get; set; }                     // jsonb
    public string? Category { get; set; }
    public bool IsDemo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
