// Domain/ValueObjects/Hit.cs —— 检索候选统一数据结构
namespace CNC_AgentCore.Domain.ValueObjects;

public sealed class Hit
{
    public string Type { get; set; } = string.Empty;       // alarm | chunk | maintenance_log
    public long Id { get; set; }
    public double Score { get; set; }
    public int Rank { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object?> Extra { get; set; } = new();

    public (string Type, long Id) Key() => (Type, Id);

    public static Hit Clone(Hit src, double? newScore = null, string? newChannel = null) => new()
    {
        Type = src.Type,
        Id = src.Id,
        Score = newScore ?? src.Score,
        Rank = src.Rank,
        Channel = newChannel ?? src.Channel,
        Title = src.Title,
        Source = src.Source,
        Content = src.Content,
        Extra = new Dictionary<string, object?>(src.Extra),
    };
}
