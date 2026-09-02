// Domain/Entities/ImportJob.cs —— kb.import_jobs
namespace CNC_AgentCore.Domain.Entities;

public sealed class ImportJob
{
    public long Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int? TotalRows { get; set; }
    public int? ProcessedRows { get; set; }
    public int? FailedRows { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
