// Domain/Abstractions/IKnowledgeEntryRepository.cs —— kb.alarms + kb.documents + kb.chunks CRUD（#17）。
namespace CNC_AgentCore.Domain.Abstractions;

// ===== 请求 record =====

public sealed record CreateAlarmRequest(
    string Brand,
    string? Controller,
    string Code,
    string Name,
    string? Description,
    string? Cause,
    string? Action,
    string? SafetyNote,
    string? Category,
    string? Severity,
    string? CreatedBy);

public sealed record UpdateAlarmRequest(
    string Name,
    string? Description,
    string? Cause,
    string? Action,
    string? SafetyNote,
    string? Category,
    string? Severity);

public sealed record UpdateFaqRequest(
    string Title,
    string Body,
    string? Brand,
    string[]? ModelScope);

/// <summary>条目管理列表查询条件（#17 GET /api/knowledge/entries）。</summary>
public sealed record KnowledgeListQuery(
    string? Type = null,        // "alarm" | "faq" | null
    string? Origin = null,      // "ingest" | "manual" | "feedback" | null
    string? Q = null,
    int Limit = 50,
    int Offset = 0);

// ===== 读取 record =====

public sealed record AlarmEntry(
    long Id,
    string Brand,
    string? Controller,
    string Code,
    string CodeNorm,
    string? Category,
    string? Severity,
    string Name,
    string? Description,
    string? Cause,
    string? Action,
    string? SafetyNote,
    string? Origin,
    string? CreatedBy,
    DateTimeOffset? CreatedAt);

public sealed record FaqEntry(
    long DocId,
    string Title,
    string? Brand,
    string[] ModelScope,
    string Body,
    string? Origin,
    string? CreatedBy,
    DateTimeOffset? CreatedAt,
    bool HasVector);

/// <summary>GET /api/knowledge/entries 的列表项；由仓库 ListEntriesAsync 构造，勿让 Dapper 直接映射。</summary>
public sealed record KnowledgeEntryListItem(
    string Type,               // "alarm" | "faq"
    long Id,                   // alarm id / faq doc_id
    long? DocId,               // 所属文档 id（alarm 恒 null；faq = Id）
    string Title,              // alarm: "品牌 码 名称" 组合串；faq: 文档标题
    string? Origin,
    string? CreatedBy,
    DateTimeOffset? CreatedAt,
    bool Vectorized);

/// <summary>kb.documents 列表单条（#20 GET /api/knowledge/documents）。</summary>
public sealed record DocumentListItem(
    long Id,
    string Title,
    string DocType,
    string? Brand,
    string Status,
    int PageCount,
    string? ErrorMsg,
    DateTimeOffset? CreatedAt);

/// <summary>kb.chunks 列表单条（#20 GET /api/knowledge/documents/{doc_id}/chunks）。</summary>
public sealed record ChunkListItem(
    long Id,
    long DocId,
    int Level,
    int Seq,
    string? HeadingPath,
    string Content,
    int ContentLen,
    int? PageFrom,
    int? PageTo,
    bool HasVector);

public interface IKnowledgeEntryRepository
{
    // —— Alarm CRUD ——

    /// <summary>插入一条 kb.alarms（手工录入），返回新 id（#17 POST /api/knowledge/entry type=alarm）。</summary>
    Task<long> InsertAlarmAsync(CreateAlarmRequest req, CancellationToken ct = default);

    /// <summary>编辑一条 kb.alarms（#17 PUT /api/knowledge/entry/alarm/{id}）。不存在返回 false。</summary>
    Task<bool> UpdateAlarmAsync(long id, UpdateAlarmRequest req, CancellationToken ct = default);

    /// <summary>删除一条 kb.alarms（#17 DELETE /api/knowledge/entry/alarm/{id}）。embedding 同行删除；不影响其他表。</summary>
    Task<bool> DeleteAlarmAsync(long id, CancellationToken ct = default);

    /// <summary>读单条 alarm（#17 编辑页用）。不存在返回 null。</summary>
    Task<AlarmEntry?> GetAlarmAsync(long id, CancellationToken ct = default);

    /// <summary>取 alarm 向量化文本（name+description+cause+action 拼接，与 #12 text_from_alarm_row 同构）。
    /// 不存在返回 null。Endpoint 拿到后调 IEmbeddingGenerator + VectorizeAlarmAsync 完成重向量化。</summary>
    Task<string?> GetAlarmVectorizeTextAsync(long id, CancellationToken ct = default);

    // —— FAQ CRUD ——

    /// <summary>插入 FAQ（已存在，#5 approve 复用）。</summary>
    Task<(long DocId, long ChunkId)> InsertFaqAsync(
        string title, string body, string? brand, string[]? modelScope,
        string? source, string? createdBy, CancellationToken ct = default);

    /// <summary>编辑 FAQ（#17 PUT /api/knowledge/entry/faq/{docId}）：更新 documents.title + 重建 chunks 内容（删旧 chunk → 插新 chunk）。</summary>
    Task<long> UpdateFaqAsync(long docId, UpdateFaqRequest req, CancellationToken ct = default);

    /// <summary>删除 FAQ 所属文档级联清父/子块 + 向量（#17 DELETE /api/knowledge/entry/faq/{docId}）。</summary>
    Task<bool> DeleteFaqAsync(long docId, CancellationToken ct = default);

    /// <summary>读 FAQ（#17 编辑页用）。不存在返回 null。</summary>
    Task<FaqEntry?> GetFaqAsync(long docId, CancellationToken ct = default);

    /// <summary>取 FAQ 向量化文本（title+body 拼接）。不存在返回 null。
    /// 主块 chunk id 通过 GetFaqPrimaryChunkIdAsync 拿，endpoint 内 embed 后 VectorizeChunkAsync 写回。</summary>
    Task<(string Text, long PrimaryChunkId)?> GetFaqVectorizeTargetAsync(long docId, CancellationToken ct = default);

    /// <summary>把 float[] 向量写入 kb.chunks.embedding（已存在，#5 approve 复用）。</summary>
    Task<bool> VectorizeChunkAsync(long chunkId, float[] vector, CancellationToken ct = default);

    // —— 列表（#17 GET /api/knowledge/entries）——

    /// <summary>条目管理统一列表（报警码 + FAQ）。</summary>
    Task<(List<KnowledgeEntryListItem> Items, int Total)> ListEntriesAsync(
        KnowledgeListQuery query, CancellationToken ct = default);

    /// <summary>把 float[] 向量写入 kb.alarms.embedding（#17 re-vectorize alarm 用）。</summary>
    Task<bool> VectorizeAlarmAsync(long alarmId, float[] vector, CancellationToken ct = default);

    // —— #20 文档上传 ——

    /// <summary>插入 kb.documents 记录（status='ready' 表示已就绪；含 title/doc_type/brand/source_file/page_count）。
    /// #20 简化：跳过 MD/PDF 解析，前端分块直接 POST chunk，文档直接 ready。</summary>
    Task<long> InsertDocumentAsync(
        string title, string docType, string? brand, string[]? modelScope,
        string? sourceFile, int pageCount, string? createdBy, CancellationToken ct = default);

    /// <summary>插入 kb.chunks 行（前端分块 POST）。doc_id/title 已知，按 (level, seq) 顺序追加。</summary>
    Task<long> InsertChunkAsync(
        long docId, int level, int seq, string content, string? headingPath,
        int? pageFrom, int? pageTo, string? origin, string? createdBy,
        CancellationToken ct = default);

    /// <summary>kb.documents 列表（按时间倒序 + 分页）。</summary>
    Task<List<DocumentListItem>> ListDocumentsAsync(int limit, int offset, CancellationToken ct = default);

    /// <summary>kb.chunks 列表（按 doc_id + seq）。</summary>
    Task<List<ChunkListItem>> ListChunksAsync(long docId, CancellationToken ct = default);

    /// <summary>删除 kb.documents（CASCADE 清 chunks + 向量）。</summary>
    Task<bool> DeleteDocumentAsync(long docId, CancellationToken ct = default);
}
