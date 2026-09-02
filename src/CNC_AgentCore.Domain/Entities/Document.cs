// Domain/Entities/Document.cs —— kb.documents
namespace CNC_AgentCore.Domain.Entities;

public sealed class Document
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? MachineModel { get; set; }
    public string? DocType { get; set; }                // manual/alarm_table/sop/faq
    public string? SourcePath { get; set; }
    public string? Hash { get; set; }
    public bool IsDemo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
