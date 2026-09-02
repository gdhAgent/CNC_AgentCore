// Application/Auth/SuggestionRepository.cs —— log.kb_suggestions 仓储（Dapper）。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Auth;

public sealed class SuggestionRepository : ISuggestionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SuggestionRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<long> InsertAsync(SuggestionRecord r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO log.kb_suggestions (
                source, trace_id, question, suggested_type, draft_content
            ) VALUES (
                @source, @traceId, @question, @suggestedType, @draftContent
            )
            RETURNING id
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("source", r.Source);
        p.Add("traceId", r.TraceId);
        p.Add("question", r.Question);
        p.Add("suggestedType", r.SuggestedType);
        p.Add("draftContent", r.DraftContent);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    // 行模型：resolved_ref 是 JSONB，强转 ::text 让 Npgsql 自动映射到 string（API 层按需解析）。
    private sealed class SuggestionRow
    {
        public long Id { get; set; }
        public string Source { get; set; } = "";
        public Guid? TraceId { get; set; }
        public string Question { get; set; } = "";
        public string SuggestedType { get; set; } = "";
        public string? DraftContent { get; set; }
        public string Status { get; set; } = "";
        public string? ResolvedRef { get; set; }
        public string? Handler { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public async Task<List<SuggestionListItem>> ListAsync(string? status, int? limit, int? offset, CancellationToken ct = default)
    {
        // open 状态优先，再按 created_at DESC
        const string sql = """
            SELECT id AS Id, source AS Source, trace_id AS TraceId, question AS Question,
                   suggested_type AS SuggestedType, draft_content AS DraftContent,
                   status AS Status, resolved_ref::text AS ResolvedRef,
                   handler AS Handler, created_at AS CreatedAt
              FROM log.kb_suggestions
             WHERE (@status::text IS NULL OR status = @status)
             ORDER BY CASE status WHEN 'open' THEN 0 ELSE 1 END, created_at DESC
             LIMIT @lim OFFSET @off
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("status", status);
        p.Add("lim", limit ?? 200);    // 默认上限 200，防全表拉爆
        p.Add("off", offset ?? 0);
        var rows = await conn.QueryAsync<SuggestionRow>(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.Select(r => new SuggestionListItem(
            r.Id, r.Source, r.TraceId, r.Question, r.SuggestedType, r.DraftContent,
            r.Status, r.ResolvedRef, r.Handler, r.CreatedAt)).ToList();
    }

    public async Task<bool> ResolveAsync(long id, Dictionary<string, object?>? resolvedRef, string? handler, CancellationToken ct = default)
    {
        // 仅 open 状态可 resolve；resolved_ref 序列化为 JSONB
        const string sql = """
            UPDATE log.kb_suggestions
               SET status = 'resolved',
                   resolved_ref = @resolvedRef::jsonb,
                   handler = @handler,
                   resolved_at = now()
             WHERE id = @id AND status = 'open'
            """;
        // null / 空 dict 都序列化为 "{}" 占位，避免 PG JSONB 反序列化报错
        var refJson = resolvedRef is null || resolvedRef.Count == 0 ? "{}" : JsonSerializer.Serialize(resolvedRef);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            sql, new { id, resolvedRef = refJson, handler }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct = default)
    {
        const string sql = "SELECT 1 FROM log.kb_suggestions WHERE id = @id";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var hit = await conn.ExecuteScalarAsync<long?>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return hit.HasValue;
    }

    public async Task<bool> RejectAsync(long id, string? handler, CancellationToken ct = default)
    {
        // 仅 open 状态可 reject；不写 resolved_ref（保留 NULL）
        const string sql = """
            UPDATE log.kb_suggestions
               SET status = 'rejected',
                   handler = @handler,
                   resolved_at = now()
             WHERE id = @id AND status = 'open'
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            sql, new { id, handler }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<SuggestionDetail?> FetchDetailAsync(long id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, source AS Source, question AS Question,
                   suggested_type AS SuggestedType, draft_content AS DraftContent,
                   status AS Status, created_at AS CreatedAt
              FROM log.kb_suggestions
             WHERE id = @id
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<SuggestionDetail>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return row;
    }

    /// <summary>knowledge 删除后，把引用它的 resolved suggestion 重新置为 open。</summary>
    public async Task<int> ReopenByResolvedRefAsync(string refType, long refId, CancellationToken ct = default)
    {
        // refType → JSON 容器过滤。alarm: {"type":"alarm","id":N}; faq: {"type":"faq","doc_id":N}
        var filter = refType switch
        {
            "alarm" => $"{{\"type\":\"alarm\",\"id\":{refId}}}",
            "faq" => $"{{\"type\":\"faq\",\"doc_id\":{refId}}}",
            _ => throw new ArgumentException($"unsupported refType for reopen: {refType}", nameof(refType)),
        };
        const string sql = """
            UPDATE log.kb_suggestions
               SET status = 'open',
                   resolved_ref = NULL,
                   resolved_at = NULL
             WHERE status = 'resolved'
               AND resolved_ref @> @filter::jsonb
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            sql, new { filter }, cancellationToken: ct));
    }
}
