// Api/Contracts/FeedbackDtos.cs —— 反馈闭环 DTO
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

// ===== POST /api/feedback =====
public sealed class FeedbackRequestDto
{
    [JsonPropertyName("trace_id")] public Guid TraceId { get; set; }
    [JsonPropertyName("verdict")] public int Verdict { get; set; } = 1;            // 1=赞 -1=踩
    [JsonPropertyName("user_code")] public string? UserCode { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }              // not_relevant|wrong_answer|incomplete|outdated|no_source|other
    [JsonPropertyName("bad_refs")] public List<int>? BadRefs { get; set; }       // 用户指出不准的引用编号
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("correction")] public string? Correction { get; set; }      // "正确答案应该是…"
}

public sealed class FeedbackResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("suggestion_id")] public long? SuggestionId { get; set; }  // verdict=-1 时自动生成
    [JsonPropertyName("message")] public string Message { get; set; } = "ok";
}

// ===== GET /api/suggestions =====
public sealed class SuggestionItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "";                    // refused|negative_feedback|manual|low_score
    [JsonPropertyName("trace_id")] public Guid? TraceId { get; set; }
    [JsonPropertyName("question")] public string Question { get; set; } = "";
    [JsonPropertyName("suggested_type")] public string SuggestedType { get; set; } = "faq";  // alarm|faq|manual_chunk|maintenance_tip
    [JsonPropertyName("draft_content")] public string? DraftContent { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "open";              // open|in_progress|resolved|rejected
    [JsonPropertyName("resolved_ref")] public Dictionary<string, object?>? ResolvedRef { get; set; }  // JSONB 解析后字典
    [JsonPropertyName("handler")] public string? Handler { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

// ===== POST /api/suggestions =====
// 手动提交待补充知识（拒答「提交为待补充知识」按钮）；source 限 refused/manual/low_score（negative_feedback 走 POST /api/feedback）。
public sealed class SuggestionCreateRequestDto
{
    [JsonPropertyName("trace_id")] public Guid TraceId { get; set; }
    [JsonPropertyName("question")] public string? Question { get; set; }       // 缺省取该 query_log.raw_query
    [JsonPropertyName("suggested_type")] public string? SuggestedType { get; set; }  // alarm|faq|manual_chunk|maintenance_tip；缺省按 detected_codes 推断
    [JsonPropertyName("draft_content")] public string? DraftContent { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "refused";     // refused|manual|low_score
}

public sealed class SuggestionCreateResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "open";
}

// ===== POST /api/suggestions/{id}/resolve =====
// 标记已处理（仅 open → resolved；resolved_ref 回写补录目标，如 {"type":"alarm","id":2048}）。
public sealed class ResolveSuggestionRequestDto
{
    [JsonPropertyName("resolved_ref")] public Dictionary<string, object?>? ResolvedRef { get; set; }
    [JsonPropertyName("handler")] public string? Handler { get; set; }
}

public sealed class ResolveSuggestionResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "resolved";
}

// ===== POST /api/suggestions/{id}/reject =====
// 拒绝建议（审核未通过）；handler 从 query string 读取，无 body DTO。
public sealed class RejectSuggestionResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "rejected";
}

// ===== POST /api/suggestions/{id}/approve =====
// 审核通过并录入知识库；现支持 alarm + faq。
public sealed class ApproveSuggestionRequestDto
{
    [JsonPropertyName("entry_type")] public string EntryType { get; set; } = "faq";     // faq | alarm
    // FAQ 字段
    [JsonPropertyName("title")] public string? Title { get; set; }                    // 缺省取 suggestion.question
    [JsonPropertyName("body")] public string? Body { get; set; }                      // 缺省取 suggestion.draft_content
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model_scope")] public List<string>? ModelScope { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }          // 审核人工号；缺省 "E1024"
    // 报警码字段（entry_type=alarm 时必填）
    [JsonPropertyName("code")] public string? Code { get; set; }                     // 报警码，如 SV0401
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }                     // 报警名
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("cause")] public string? Cause { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("safety_note")] public string? SafetyNote { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    // alarm 录入品牌的可选项
    [JsonPropertyName("alarm_brand")] public string? AlarmBrand { get; set; }         // 品牌：FANUC/MITSUBISHI/...；缺省取 suggestion.question 推断或 "GENERIC"
}

public sealed class ApproveSuggestionResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "resolved";
    [JsonPropertyName("entry_type")] public string EntryType { get; set; } = "faq";
    [JsonPropertyName("doc_id")] public long DocId { get; set; }
    [JsonPropertyName("chunk_id")] public long ChunkId { get; set; }
    [JsonPropertyName("vectorized")] public bool Vectorized { get; set; }
}
