// Domain/Abstractions/ISuggestionRepository.cs —— log.kb_suggestions 仓储
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>写入 log.kb_suggestions 所需字段（#1）。</summary>
public sealed record SuggestionRecord(
    string Source,                                // refused|negative_feedback|manual|low_score
    string Question,
    Guid? TraceId = null,
    string SuggestedType = "faq",                 // alarm|faq|manual_chunk|maintenance_tip
    string? DraftContent = null);

/// <summary>列出/导出 log.kb_suggestions 用（#2）。ResolvedRef 保留 raw JSON 字符串，API 层按需解析。</summary>
public sealed record SuggestionListItem(
    long Id,
    string Source,
    Guid? TraceId,
    string Question,
    string SuggestedType,
    string? DraftContent,
    string Status,                                // open|in_progress|resolved|rejected
    string? ResolvedRef,                          // JSONB 序列化字符串，如 {"type":"alarm","id":2048}
    string? Handler,
    DateTimeOffset CreatedAt);

/// <summary>单条建议详情（#5 approve 用，需 status + question + draft_content 推断 title/body）。</summary>
public sealed record SuggestionDetail(
    long Id,
    string Source,
    string Question,
    string SuggestedType,
    string? DraftContent,
    string Status,
    DateTimeOffset CreatedAt);

public interface ISuggestionRepository
{
    /// <summary>插入一条 log.kb_suggestions（status 默认 'open'），返回新 id。</summary>
    Task<long> InsertAsync(SuggestionRecord record, CancellationToken ct = default);

    /// <summary>列出建议清单（status 可选过滤；open 优先，再 created_at DESC；limit/offset 可选，不强制分页）。</summary>
    Task<List<SuggestionListItem>> ListAsync(string? status, int? limit, int? offset, CancellationToken ct = default);

    /// <summary>标记已处理：仅 status='open' 可 resolve → resolved；返回是否成功。</summary>
    Task<bool> ResolveAsync(long id, Dictionary<string, object?>? resolvedRef, string? handler, CancellationToken ct = default);

    /// <summary>拒绝建议：仅 status='open' 可 reject → rejected（不写 resolved_ref）；返回是否成功。</summary>
    Task<bool> RejectAsync(long id, string? handler, CancellationToken ct = default);

    /// <summary>判断 suggestion 是否存在（用于 404 / 409 区分）。</summary>
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);

    /// <summary>取单条 suggestion 详情（approve 用）；不存在返回 null。</summary>
    Task<SuggestionDetail?> FetchDetailAsync(long id, CancellationToken ct = default);

    /// <summary>知识条目被删除时，将其对应的 resolved suggestion 重开为 open。
    /// refType ∈ {"alarm","faq"}，refId 为 alarm.id 或 faq 的 doc_id；返回重开数。</summary>
    Task<int> ReopenByResolvedRefAsync(string refType, long refId, CancellationToken ct = default);
}
