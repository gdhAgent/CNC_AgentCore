// Domain/Abstractions/IImportJobRepository.cs —— 知识导入任务追踪（#18）。
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>导入任务记录（#18 POST /api/knowledge/import/{job_id}/confirm）。</summary>
public sealed record ImportJobRecord(
    long Id,
    string JobType,         // alarm | faq | machine | maintenance
    string Filename,
    string? FileHash,
    int TotalRows,
    int ValidRows,
    int DupRows,
    int ErrorRows,
    int ImportedRows,
    int Vectorized,
    string DupStrategy,     // skip | overwrite | duplicate
    string Status,          // validating | previewing | importing | done | failed | cancelled
    string Errors,          // JSON 字符串 [{row,field,reason}]
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

public sealed record ImportJobPreviewRequest(
    string JobType,
    string Filename,
    string? FileHash,
    int TotalRows,
    int ValidRows,
    int DupRows,
    int ErrorRows,
    string DupStrategy,
    string Errors,
    string? CreatedBy);

public sealed record ImportJobProgressUpdate(
    int ImportedRows,
    int Vectorized,
    string Status,
    DateTimeOffset? FinishedAt);

public interface IImportJobRepository
{
    Task<long> InsertPreviewAsync(ImportJobPreviewRequest req, CancellationToken ct = default);
    Task<ImportJobRecord?> GetAsync(long jobId, CancellationToken ct = default);
    Task UpdateProgressAsync(long jobId, ImportJobProgressUpdate update, CancellationToken ct = default);
    Task<List<ImportJobRecord>> ListAsync(int limit, int offset, CancellationToken ct = default);
}
