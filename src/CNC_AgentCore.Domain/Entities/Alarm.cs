// Domain/Entities/Alarm.cs —— kb.alarms
namespace CNC_AgentCore.Domain.Entities;

public sealed class Alarm
{
    public long Id { get; set; }
    public string? Brand { get; set; }                 // FANUC/MITSUBISHI/SIEMENS
    public string? Controller { get; set; }            // 0i-MF/M80/...
    public string CodeNorm { get; set; } = string.Empty;   // 归一化码 SV0401
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public string? Description { get; set; }
    public string? Cause { get; set; }
    public string? Action { get; set; }
    public string? SafetyNote { get; set; }
    public float[]? Embedding { get; set; }             // pgvector(1024)
    public string? Tsv { get; set; }                    // tsvector
    public bool IsDemo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
