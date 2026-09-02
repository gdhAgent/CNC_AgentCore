// Application/Import/ImportJobRepository.cs —— 导入任务数据访问。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Import;

public sealed class ImportJobRepository : IImportJobRepository
{
    private readonly NpgsqlDataSource _ds;

    public ImportJobRepository(NpgsqlDataSource ds) => _ds = ds;

    private sealed class Row
    {
        public long Id { get; set; }
        public string JobType { get; set; } = "";
        public string Filename { get; set; } = "";
        public string? FileHash { get; set; }
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int DupRows { get; set; }
        public int ErrorRows { get; set; }
        public int ImportedRows { get; set; }
        public int Vectorized { get; set; }
        public string DupStrategy { get; set; } = "skip";
        public string Status { get; set; } = "validating";
        public string Errors { get; set; } = "[]";        // JSONB ::text
        public string? CreatedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }

    public async Task<long> InsertPreviewAsync(ImportJobPreviewRequest r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO kb.import_jobs (
                job_type, filename, file_hash, total_rows, valid_rows, dup_rows, error_rows,
                dup_strategy, status, errors, created_by, created_at
            ) VALUES (
                @jobType, @filename, @fileHash, @totalRows, @validRows, @dupRows, @errorRows,
                @dupStrategy, 'previewing', @errors::jsonb, @createdBy, now()
            )
            RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("jobType", r.JobType);
        p.Add("filename", r.Filename);
        p.Add("fileHash", r.FileHash);
        p.Add("totalRows", r.TotalRows);
        p.Add("validRows", r.ValidRows);
        p.Add("dupRows", r.DupRows);
        p.Add("errorRows", r.ErrorRows);
        p.Add("dupStrategy", r.DupStrategy);
        p.Add("errors", r.Errors);
        p.Add("createdBy", r.CreatedBy);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    public async Task<ImportJobRecord?> GetAsync(long jobId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, job_type AS JobType, filename, file_hash AS FileHash, total_rows AS TotalRows, valid_rows AS ValidRows, dup_rows AS DupRows, error_rows AS ErrorRows,
                   imported_rows AS ImportedRows, vectorized, dup_strategy AS DupStrategy, status,
                   errors::text AS errors, created_by AS CreatedBy, created_at AS CreatedAt, finished_at AS FinishedAt
              FROM kb.import_jobs WHERE id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            sql, new { id = jobId }, cancellationToken: ct));
        if (row is null) return null;
        return new ImportJobRecord(
            row.Id, row.JobType, row.Filename, row.FileHash, row.TotalRows, row.ValidRows,
            row.DupRows, row.ErrorRows, row.ImportedRows, row.Vectorized, row.DupStrategy,
            row.Status, row.Errors, row.CreatedBy, row.CreatedAt, row.FinishedAt);
    }

    public async Task UpdateProgressAsync(long jobId, ImportJobProgressUpdate u, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE kb.import_jobs SET
                imported_rows = @imported,
                vectorized = @vectorized,
                status = @status,
                finished_at = COALESCE(@finishedAt, finished_at)
            WHERE id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("id", jobId);
        p.Add("imported", u.ImportedRows);
        p.Add("vectorized", u.Vectorized);
        p.Add("status", u.Status);
        p.Add("finishedAt", u.FinishedAt);
        await conn.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    public async Task<List<ImportJobRecord>> ListAsync(int limit, int offset, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, job_type AS JobType, filename, file_hash AS FileHash, total_rows AS TotalRows, valid_rows AS ValidRows, dup_rows AS DupRows, error_rows AS ErrorRows,
                   imported_rows AS ImportedRows, vectorized, dup_strategy AS DupStrategy, status,
                   errors::text AS errors, created_by AS CreatedBy, created_at AS CreatedAt, finished_at AS FinishedAt
              FROM kb.import_jobs
             ORDER BY id DESC
             LIMIT @lim OFFSET @off
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            sql, new { lim = limit, off = offset }, cancellationToken: ct));
        return rows.Select(row => new ImportJobRecord(
            row.Id, row.JobType, row.Filename, row.FileHash, row.TotalRows, row.ValidRows,
            row.DupRows, row.ErrorRows, row.ImportedRows, row.Vectorized, row.DupStrategy,
            row.Status, row.Errors, row.CreatedBy, row.CreatedAt, row.FinishedAt)).ToList();
    }
}
