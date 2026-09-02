// Domain/Entities/Suggestion.cs —— log.kb_suggestions（待补充知识清单）
namespace CNC_AgentCore.Domain.Entities;

public sealed class Suggestion
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;     // feedback/query/refused
    public Guid? SourceTraceId { get; set; }
    public string? OriginalQuery { get; set; }
    public string? OriginalAnswer { get; set; }
    public string? SuggestedTitle { get; set; }
    public string? SuggestedContent { get; set; }
    public string Status { get; set; } = "open";          // open/approved/rejected/resolved
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ResolvedRef { get; set; }               // 录入后的 entry ref
    public DateTimeOffset CreatedAt { get; set; }
}
