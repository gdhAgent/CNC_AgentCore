// Domain/Entities/Feedback.cs —— log.feedbacks（用户反馈）
namespace CNC_AgentCore.Domain.Entities;

public sealed class Feedback
{
    public long Id { get; set; }
    public Guid TraceId { get; set; }
    public int Rating { get; set; }                       // -1 / 0 / 1
    public string? Comment { get; set; }
    public string? Username { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
