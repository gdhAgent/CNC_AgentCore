// Domain/Abstractions/IFeedbackRepository.cs —— log.feedbacks 仓储
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>写入 log.feedbacks 所需字段；query_log_id 由调用方按 trace_id 解析后填入。</summary>
public sealed record FeedbackRecord(
    Guid TraceId,
    int QueryLogId,
    int Verdict,                                  // 1 | -1
    string? UserCode = null,
    string? Reason = null,                        // not_relevant|wrong_answer|incomplete|outdated|no_source|other
    int[]? BadRefs = null,
    string? Comment = null,
    string? Correction = null);

public interface IFeedbackRepository
{
    /// <summary>插入一条 log.feedbacks，返回新 id。</summary>
    Task<long> InsertAsync(FeedbackRecord record, CancellationToken ct = default);

    /// <summary>回写 log.query_logs.feedback / feedback_note 汇总列（看板 / 列表筛选用）。</summary>
    Task<bool> UpdateQueryLogFeedbackAsync(Guid traceId, int verdict, string? note, CancellationToken ct = default);

    /// <summary>取某次问答的最新 verdict（按 id desc 取 1 条）；无反馈返回 null。</summary>
    Task<int?> GetLatestVerdictByTraceAsync(Guid traceId, CancellationToken ct = default);
}
