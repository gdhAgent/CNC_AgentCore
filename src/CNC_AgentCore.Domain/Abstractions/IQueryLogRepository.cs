// Domain/Abstractions/IQueryLogRepository.cs —— log 层落库
namespace CNC_AgentCore.Domain.Abstractions;

public sealed record QueryLogRecord(
    Guid TraceId,
    string RawQuery,
    string Route,
    List<string> DetectedCodes,
    List<Dictionary<string, object?>> RetrievedSnapshot,
    double? TopScore,
    bool Refused,
    int LatencyMs,
    Dictionary<string, int> LatencyBreakdown,
    string? Answer = null,
    string? SessionId = null,
    string? UserCode = null,
    List<Dictionary<string, object?>>? ToolCalls = null,
    int? PromptTokens = null,
    int? CompletionTokens = null);

/// <summary>按 trace_id 查 query_log 最小信息（Feedback / Trace 端点共用）。</summary>
public sealed record QueryLogInfo(long Id, string RawQuery, List<string> DetectedCodes);

/// <summary>trace 排查页所需的 query_log 全字段（#8）。JSONB 字段在 Repository 内反序列化为 Dictionary。</summary>
public sealed record QueryLogFullDetail(
    long Id,
    Guid TraceId,
    string RawQuery,
    string Route,
    bool Refused,
    List<string> DetectedCodes,
    string? Answer,
    int? LatencyMs,
    Dictionary<string, int> LatencyBreakdown,
    List<Dictionary<string, object?>> ToolCalls,
    DateTimeOffset CreatedAt);

/// <summary>单条 trace step（#8），按 seq 升序输出。</summary>
public sealed record TraceStepRow(
    int Seq,
    string Step,
    string Status,
    DateTimeOffset StartedAt,
    int Ms,
    Dictionary<string, object?> Input,
    Dictionary<string, object?> Output,
    string? Note);

/// <summary>日志列表单条（#9）。</summary>
public sealed record QueryLogListItem(
    long Id,
    Guid TraceId,
    string RawQuery,
    string Route,
    bool Refused,
    int? Feedback,
    int? LatencyMs,
    string? UserCode,
    DateTimeOffset? CreatedAt);

/// <summary>日志列表查询条件（#9）。
/// Feedback 取值约定：
///   - FeedbackAny=true → 取所有 feedback IS NOT NULL（无视 Feedback）
///   - FeedbackAny=false 且 Feedback=null → 不限 feedback
///   - FeedbackAny=false 且 Feedback=1 / -1 → feedback = @feedback
/// Route 走白名单（exact_code/hybrid/refused/agent/rag_fallback）；q 走 ILIKE '%q%'；时间按 created_at。</summary>
public sealed record QueryLogListQuery(
    bool? Refused = null,
    int? Feedback = null,
    bool FeedbackAny = false,
    string? Route = null,
    string? UserCode = null,
    string? Q = null,
    DateTimeOffset? FromTime = null,
    DateTimeOffset? ToTime = null,
    int Limit = 20,
    int Offset = 0);

public interface IQueryLogRepository
{
    /// <summary>插入一条 log.query_logs，返回新 id。</summary>
    Task<long> InsertAsync(QueryLogRecord record, CancellationToken ct = default);

    /// <summary>批量写 log.query_trace_steps（seq 从 1 起），返回写入条数。</summary>
    Task<int> InsertTraceStepsAsync(long queryLogId, Guid traceId, IReadOnlyList<Dictionary<string, object?>> steps, CancellationToken ct = default);

    /// <summary>按 trace_id 查主记录最小字段；不存在返回 null。</summary>
    Task<QueryLogInfo?> GetInfoByTraceAsync(Guid traceId, CancellationToken ct = default);

    /// <summary>按 trace_id 查主记录全字段（trace 排查页用）；不存在返回 null。</summary>
    Task<QueryLogFullDetail?> GetFullDetailAsync(Guid traceId, CancellationToken ct = default);

    /// <summary>按 trace_id 查全量 trace_steps（按 seq 升序）。</summary>
    Task<List<TraceStepRow>> GetTraceStepsAsync(Guid traceId, CancellationToken ct = default);

    /// <summary>日志列表（#9），筛选 + 分页 + total。ORDER BY id DESC。</summary>
    Task<(List<QueryLogListItem> Items, int Total)> ListAsync(QueryLogListQuery query, CancellationToken ct = default);
}
