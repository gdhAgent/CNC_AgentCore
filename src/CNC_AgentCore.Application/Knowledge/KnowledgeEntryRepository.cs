// Application/Knowledge/KnowledgeEntryRepository.cs —— kb.alarms + kb.documents + kb.chunks CRUD。
// pgvector 写 float[] 一律用 "[a,b,...]" + ::vector 强转。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Knowledge;

public sealed class KnowledgeEntryRepository : IKnowledgeEntryRepository
{
    private readonly NpgsqlDataSource _ds;

    public KnowledgeEntryRepository(NpgsqlDataSource ds) => _ds = ds;

    // ===== Alarm CRUD =====

    public async Task<long> InsertAlarmAsync(CreateAlarmRequest r, CancellationToken ct = default)
    {
        // code_norm：去前导零 + 大写
        var codeNorm = (r.Code ?? "").TrimStart('0').ToUpperInvariant();
        if (string.IsNullOrEmpty(codeNorm)) codeNorm = (r.Code ?? "").ToUpperInvariant();

        const string sql = """
            INSERT INTO kb.alarms (
                brand, controller, code, code_norm, category, severity, name,
                description, cause, action, safety_note, origin, created_by, created_at
            ) VALUES (
                @brand, @controller, @code, @codeNorm, @category, COALESCE(@severity, 'unknown'), @name,
                @description, @cause, @action, @safetyNote, 'manual', @createdBy, now()
            )
            ON CONFLICT (brand, COALESCE(controller, ''), code_norm) DO NOTHING
            RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("brand", r.Brand);
        p.Add("controller", r.Controller);
        p.Add("code", r.Code);
        p.Add("codeNorm", codeNorm);
        p.Add("category", r.Category);
        p.Add("severity", r.Severity);
        p.Add("name", r.Name);
        p.Add("description", r.Description);
        p.Add("cause", r.Cause);
        p.Add("action", r.Action);
        p.Add("safetyNote", r.SafetyNote);
        p.Add("createdBy", r.CreatedBy);
        var id = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, p, cancellationToken: ct));
        // ON CONFLICT DO NOTHING 返回 null 表示已存在 → 抛错（端点层 catch 转 409）
        if (id is null)
            throw new InvalidOperationException(
                $"alarm 已存在 (brand={r.Brand}, controller={r.Controller}, code_norm={codeNorm})");
        return id.Value;
    }

    public async Task<bool> UpdateAlarmAsync(long id, UpdateAlarmRequest r, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE kb.alarms SET
                name = @name,
                description = @description,
                cause = @cause,
                action = @action,
                safety_note = @safetyNote,
                category = @category,
                severity = COALESCE(@severity, severity)
            WHERE id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            id,
            r.Name,
            r.Description,
            r.Cause,
            r.Action,
            r.SafetyNote,
            r.Category,
            r.Severity,
        }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> DeleteAlarmAsync(long id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM kb.alarms WHERE id = @id RETURNING id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id }, cancellationToken: ct));
        return deleted.HasValue;
    }

    private sealed class AlarmRow
    {
        public long Id { get; set; }
        public string Brand { get; set; } = "";
        public string? Controller { get; set; }
        public string Code { get; set; } = "";
        public string CodeNorm { get; set; } = "";
        public string? Category { get; set; }
        public string? Severity { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Cause { get; set; }
        public string? Action { get; set; }
        public string? SafetyNote { get; set; }
        public string? Origin { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    public async Task<AlarmEntry?> GetAlarmAsync(long id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, brand, controller, code, code_norm AS CodeNorm, category, severity, name,
                   description, cause, action, safety_note AS SafetyNote, origin, created_by AS CreatedBy, created_at AS CreatedAt
              FROM kb.alarms WHERE id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<AlarmRow>(new CommandDefinition(
            sql, new { id }, cancellationToken: ct));
        if (row is null) return null;
        return new AlarmEntry(row.Id, row.Brand, row.Controller, row.Code, row.CodeNorm,
            row.Category, row.Severity, row.Name, row.Description, row.Cause, row.Action,
            row.SafetyNote, row.Origin, row.CreatedBy, row.CreatedAt);
    }

    public async Task<bool> VectorizeAlarmAsync(long alarmId, float[] vector, CancellationToken ct = default)
    {
        var vecLiteral = "[" + string.Join(",", vector.Select(v => v.ToString("G9"))) + "]";
        const string sql = """
            UPDATE kb.alarms SET embedding = @vec::vector WHERE id = @id RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var updated = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id = alarmId, vec = vecLiteral }, cancellationToken: ct));
        return updated.HasValue;
    }

    public async Task<string?> GetAlarmVectorizeTextAsync(long id, CancellationToken ct = default)
    {
        // 文本构造与 VectorizeService.BuildAlarmText 同一套
        const string sql = """
            SELECT brand, controller, code, name, description, cause, action, safety_note
              FROM kb.alarms WHERE id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<(string Brand, string? Controller, string Code, string Name,
            string? Description, string? Cause, string? Action, string? SafetyNote)?>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        if (row is null) return null;
        var r = row.Value;
        var head = $"[{r.Brand}][{r.Controller ?? ""}] 报警{r.Code} {r.Name}".Trim();
        var parts = new List<string> { head + "。" };
        if (!string.IsNullOrEmpty(r.Description)) parts.Add($"现象：{r.Description}");
        if (!string.IsNullOrEmpty(r.Cause)) parts.Add($"原因：\n{r.Cause}");
        if (!string.IsNullOrEmpty(r.Action)) parts.Add($"处置：\n{r.Action}");
        return string.Join("\n", parts);
    }

    // ===== FAQ CRUD =====

    public async Task<(long DocId, long ChunkId)> InsertFaqAsync(
        string title, string body, string? brand, string[]? modelScope,
        string? source, string? createdBy, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            const string docSql = """
                INSERT INTO kb.documents (
                    title, doc_type, brand, model_scope, source_file, status, lang, created_at, updated_at
                ) VALUES (
                    @title, 'faq', @brand, @modelScope, '', 'ready', 'zh', now(), now()
                )
                RETURNING id
                """;
            var scope = modelScope ?? Array.Empty<string>();
            var docId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(docSql,
                new { title, brand, modelScope = scope }, transaction: tx, cancellationToken: ct));

            const string chunkSql = """
                INSERT INTO kb.chunks (
                    doc_id, level, seq, content, content_len, tsv, origin, created_by, created_at
                ) VALUES (
                    @docId, 1, 1, @content, @contentLen,
                    to_tsvector('simple', coalesce(@content, '')),
                    'manual', @createdBy, now()
                )
                RETURNING id
                """;
            var chunkId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(chunkSql,
                new { docId, content = body, contentLen = body.Length, createdBy },
                transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return (docId, chunkId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<long> UpdateFaqAsync(long docId, UpdateFaqRequest r, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) UPDATE kb.documents
            const string docSql = """
                UPDATE kb.documents SET title = @title, brand = @brand, model_scope = @modelScope, updated_at = now()
                 WHERE id = @docId AND doc_type = 'faq'
                """;
            var scope = r.ModelScope ?? Array.Empty<string>();
            var docAffected = await conn.ExecuteAsync(new CommandDefinition(docSql,
                new { docId, title = r.Title, brand = r.Brand, modelScope = scope },
                transaction: tx, cancellationToken: ct));
            if (docAffected == 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }

            // 2) DELETE 旧 chunks
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM kb.chunks WHERE doc_id = @docId",
                new { docId }, transaction: tx, cancellationToken: ct));

            // 3) INSERT 新 chunk (level=1, origin='manual' 编辑后保持 manual)
            const string chunkSql = """
                INSERT INTO kb.chunks (
                    doc_id, level, seq, content, content_len, tsv, origin, created_at
                ) VALUES (
                    @docId, 1, 1, @content, @contentLen,
                    to_tsvector('simple', coalesce(@content, '')),
                    'manual', now()
                )
                RETURNING id
                """;
            var newChunkId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(chunkSql,
                new { docId, content = r.Body, contentLen = r.Body.Length },
                transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return newChunkId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> DeleteFaqAsync(long docId, CancellationToken ct = default)
    {
        // CASCADE 自动清父/子 chunks + 向量（documents.doc_id FK ON DELETE CASCADE）
        const string sql = "DELETE FROM kb.documents WHERE id = @docId AND doc_type = 'faq' RETURNING id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { docId }, cancellationToken: ct));
        return deleted.HasValue;
    }

    private sealed class FaqRow
    {
        public long DocId { get; set; }
        public string Title { get; set; } = "";
        public string? Brand { get; set; }
        public string[] ModelScope { get; set; } = Array.Empty<string>();
        public string Body { get; set; } = "";
        public string? Origin { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public bool HasVector { get; set; }
    }

    public async Task<FaqEntry?> GetFaqAsync(long docId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT d.id AS DocId, d.title, d.brand, d.model_scope AS ModelScope, d.created_at AS CreatedAt, d.created_by AS CreatedBy,
                   c.content AS body, c.origin,
                   EXISTS(SELECT 1 FROM kb.chunks WHERE doc_id = d.id AND embedding IS NOT NULL) AS HasVector
              FROM kb.documents d
              LEFT JOIN kb.chunks c ON c.doc_id = d.id AND c.level = 1
             WHERE d.id = @docId AND d.doc_type = 'faq'
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<FaqRow>(new CommandDefinition(
            sql, new { docId }, cancellationToken: ct));
        if (row is null) return null;
        return new FaqEntry(row.DocId, row.Title, row.Brand, row.ModelScope ?? Array.Empty<string>(),
            row.Body ?? "", row.Origin, row.CreatedBy, row.CreatedAt, row.HasVector);
    }

    public async Task<(string Text, long PrimaryChunkId)?> GetFaqVectorizeTargetAsync(long docId, CancellationToken ct = default)
    {
        // 主块 level=1（FAQ 只插 1 块）
        const string sql = """
            SELECT d.title, c.content, c.id AS chunk_id
              FROM kb.documents d
              JOIN kb.chunks c ON c.doc_id = d.id AND c.level = 1
             WHERE d.id = @docId AND d.doc_type = 'faq'
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<(string Title, string Content, long ChunkId)?>(
            new CommandDefinition(sql, new { docId }, cancellationToken: ct));
        if (row is null) return null;
        var text = $"{row.Value.Title}\n{row.Value.Content}".Trim();
        return (text, row.Value.ChunkId);
    }

    public async Task<bool> VectorizeChunkAsync(long chunkId, float[] vector, CancellationToken ct = default)
    {
        var vecLiteral = "[" + string.Join(",", vector.Select(v => v.ToString("G9"))) + "]";
        const string sql = "UPDATE kb.chunks SET embedding = @vec::vector WHERE id = @id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            sql, new { id = chunkId, vec = vecLiteral }, cancellationToken: ct));
        return affected > 0;
    }

    // ===== 文档 =====

    public async Task<long> InsertDocumentAsync(
        string title, string docType, string? brand, string[]? modelScope,
        string? sourceFile, int pageCount, string? createdBy, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO kb.documents (
                title, doc_type, brand, model_scope, source_file, page_count, status, lang, created_at, updated_at
            ) VALUES (
                @title, @docType, @brand, @modelScope, @sourceFile, @pageCount, 'ready', 'zh', now(), now()
            )
            RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var scope = modelScope ?? Array.Empty<string>();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql,
            new { title, docType, brand, modelScope = scope, sourceFile, pageCount, createdBy },
            cancellationToken: ct));
    }

    public async Task<long> InsertChunkAsync(
        long docId, int level, int seq, string content, string? headingPath,
        int? pageFrom, int? pageTo, string? origin, string? createdBy,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO kb.chunks (
                doc_id, level, seq, content, content_len, heading_path, page_from, page_to,
                tsv, origin, created_by, created_at
            ) VALUES (
                @docId, @level, @seq, @content, @contentLen, @headingPath, @pageFrom, @pageTo,
                to_tsvector('simple', coalesce(@content, '')), @origin, @createdBy, now()
            )
            RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("docId", docId);
        p.Add("level", level);
        p.Add("seq", seq);
        p.Add("content", content);
        p.Add("contentLen", content.Length);
        p.Add("headingPath", headingPath);
        p.Add("pageFrom", pageFrom);
        p.Add("pageTo", pageTo);
        p.Add("origin", origin ?? "manual");
        p.Add("createdBy", createdBy);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    private sealed class DocumentRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string DocType { get; set; } = "";
        public string? Brand { get; set; }
        public string Status { get; set; } = "pending";
        public int PageCount { get; set; }
        public string? ErrorMsg { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    public async Task<List<DocumentListItem>> ListDocumentsAsync(int limit, int offset, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, title, doc_type AS doctype, brand, status,
                   page_count AS pagecount, error_msg AS errormsg, created_at AS createdat
              FROM kb.documents
             ORDER BY id DESC LIMIT @lim OFFSET @off
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DocumentRow>(new CommandDefinition(
            sql, new { lim = limit, off = offset }, cancellationToken: ct));
        return rows.Select(r => new DocumentListItem(
            r.Id, r.Title, r.DocType, r.Brand, r.Status, r.PageCount, r.ErrorMsg, r.CreatedAt)).ToList();
    }

    private sealed class ChunkRow2
    {
        public long Id { get; set; }
        public long DocId { get; set; }
        public int Level { get; set; }
        public int Seq { get; set; }
        public string? HeadingPath { get; set; }
        public string Content { get; set; } = "";
        public int ContentLen { get; set; }
        public int? PageFrom { get; set; }
        public int? PageTo { get; set; }
        public bool HasVector { get; set; }
    }

    public async Task<List<ChunkListItem>> ListChunksAsync(long docId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, doc_id AS docid, level, seq, heading_path AS headingpath, content,
                   content_len AS contentlen, page_from AS pagefrom, page_to AS pageto,
                   (embedding IS NOT NULL) AS hasvector
              FROM kb.chunks WHERE doc_id = @docId ORDER BY level, seq
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ChunkRow2>(new CommandDefinition(
            sql, new { docId }, cancellationToken: ct));
        return rows.Select(r => new ChunkListItem(
            r.Id, r.DocId, r.Level, r.Seq, r.HeadingPath, r.Content, r.ContentLen,
            r.PageFrom, r.PageTo, r.HasVector)).ToList();
    }

    public async Task<bool> DeleteDocumentAsync(long docId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM kb.documents WHERE id = @id RETURNING id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id = docId }, cancellationToken: ct));
        return deleted.HasValue;
    }

    // ===== 列表 =====

    /// <summary>ListEntries 查出的行；SQL 列别名必须与这些属性名单 token 一致（Dapper 不剥下划线）。</summary>
    private sealed class EntryRow
    {
        public string Type { get; set; } = "";
        public long Id { get; set; }
        public long? DocId { get; set; }
        public string Title { get; set; } = "";
        public string? Origin { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public bool Vectorized { get; set; }
    }

    private static KnowledgeEntryListItem ToEntry(EntryRow r) => new(
        r.Type, r.Id, r.DocId, r.Title, r.Origin, r.CreatedBy, r.CreatedAt, r.Vectorized);

    public async Task<(List<KnowledgeEntryListItem> Items, int Total)> ListEntriesAsync(
        KnowledgeListQuery q, CancellationToken ct = default)
    {
        // 报警码 + FAQ 两表分查合并，按 type/origin/q 筛选；item 字段见 KnowledgeEntryListItem。
        var hasType = !string.IsNullOrEmpty(q.Type);

        // alarm 的 q 命中 name/code_norm/code
        var alarmCond = "(@origin::text IS NULL OR origin = @origin)"
            + " AND (@q::text IS NULL OR name ILIKE '%' || @q || '%'"
            + "  OR code_norm ILIKE '%' || @q || '%' OR code ILIKE '%' || @q || '%')";
        // faq 的 q 命中 title 或正文 chunk
        var faqCond = "(@origin::text IS NULL OR c.origin = @origin)"
            + " AND (@q::text IS NULL OR d.title ILIKE '%' || @q || '%'"
            + "  OR EXISTS(SELECT 1 FROM kb.chunks ccq WHERE ccq.doc_id = d.id AND ccq.content ILIKE '%' || @q || '%'))";

        // 列别名单 token，Dapper 不剥下划线，须与 C# 属性名一致
        var alarmListSql = $$"""
            SELECT 'alarm' AS type, id, NULL::bigint AS docid,
                   trim(concat_ws(' ', brand, code_norm, name)) AS title,
                   origin, created_by AS createdby, created_at AS createdat,
                   (embedding IS NOT NULL) AS vectorized
              FROM kb.alarms
             WHERE {{alarmCond}}
             ORDER BY id DESC LIMIT @lim OFFSET @off
            """;
        var faqListSql = $$"""
            SELECT 'faq' AS type, d.id AS id, d.id AS docid, d.title AS title,
                   c.origin AS origin, c.created_by AS createdby,
                   d.created_at AS createdat,
                   EXISTS(SELECT 1 FROM kb.chunks cc
                           WHERE cc.doc_id = d.id AND cc.embedding IS NOT NULL) AS vectorized
              FROM kb.documents d
              LEFT JOIN kb.chunks c ON c.doc_id = d.id AND c.level = 1
             WHERE d.doc_type = 'faq' AND {{faqCond}}
             ORDER BY d.id DESC LIMIT @lim OFFSET @off
            """;

        var alarmCountSql = $"SELECT count(*) FROM kb.alarms WHERE {alarmCond}";
        var faqCountSql = $"SELECT count(*) FROM kb.documents d LEFT JOIN kb.chunks c ON c.doc_id = d.id AND c.level = 1 WHERE d.doc_type = 'faq' AND {faqCond}";

        var p = new DynamicParameters();
        p.Add("origin", q.Origin);
        p.Add("q", q.Q);
        p.Add("lim", q.Limit);
        p.Add("off", q.Offset);

        await using var conn = await _ds.OpenConnectionAsync(ct);

        var items = new List<KnowledgeEntryListItem>();
        var total = 0;

        if (hasType && q.Type == "alarm")
        {
            total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(alarmCountSql, p, cancellationToken: ct));
            var rows = await conn.QueryAsync<EntryRow>(new CommandDefinition(alarmListSql, p, cancellationToken: ct));
            items.AddRange(rows.Select(ToEntry));
        }
        else if (hasType && q.Type == "faq")
        {
            total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(faqCountSql, p, cancellationToken: ct));
            var rows = await conn.QueryAsync<EntryRow>(new CommandDefinition(faqListSql, p, cancellationToken: ct));
            items.AddRange(rows.Select(ToEntry));
        }
        else
        {
            // "全部"分支：两表各取 max(limit,1000) 条（OFFSET 0，按 id DESC），合并按 created_at 倒序后内存切片 [offset, offset+limit)
            var alarmCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(alarmCountSql, p, cancellationToken: ct));
            var faqCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(faqCountSql, p, cancellationToken: ct));
            total = alarmCount + faqCount;

            var big = Math.Max(q.Limit, 1000);
            var bigP = new DynamicParameters();
            bigP.Add("origin", q.Origin);
            bigP.Add("q", q.Q);
            bigP.Add("lim", big);
            bigP.Add("off", 0);
            var aRows = (await conn.QueryAsync<EntryRow>(new CommandDefinition(alarmListSql, bigP, cancellationToken: ct))).ToList();
            var fRows = (await conn.QueryAsync<EntryRow>(new CommandDefinition(faqListSql, bigP, cancellationToken: ct))).ToList();
            items = aRows.Concat(fRows).Select(ToEntry)
                .OrderByDescending(x => x.CreatedAt)
                .Skip(q.Offset)
                .Take(q.Limit)
                .ToList();
        }
        return (items, total);
    }
}
