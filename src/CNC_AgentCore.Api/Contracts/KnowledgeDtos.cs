// Api/Contracts/KnowledgeDtos.cs —— 知识库条目 CRUD 端点契约（#17 Knowledge A1）。
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

// ===== Alarm =====

public sealed class CreateAlarmRequestDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "alarm";
    [JsonPropertyName("brand")] public string Brand { get; set; } = "";
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("cause")] public string? Cause { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("safety_note")] public string? SafetyNote { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
}

public sealed class UpdateAlarmRequestDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("cause")] public string? Cause { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("safety_note")] public string? SafetyNote { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
}

public sealed class AlarmEntryDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "alarm";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("brand")] public string Brand { get; set; } = "";
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("cause")] public string? Cause { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("safety_note")] public string? SafetyNote { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("severity")] public string? Severity { get; set; }
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

// ===== FAQ =====

public sealed class CreateFaqRequestDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "faq";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model_scope")] public string[]? ModelScope { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
}

public sealed class UpdateFaqRequestDto
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model_scope")] public string[]? ModelScope { get; set; }
}

public sealed class FaqEntryDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "faq";
    [JsonPropertyName("doc_id")] public long DocId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model_scope")] public string[] ModelScope { get; set; } = Array.Empty<string>();
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("has_vector")] public bool HasVector { get; set; }
}

// ===== 通用响应 =====

public sealed class EntryResponseDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("vectorized")] public bool Vectorized { get; set; }
}

public sealed class DeleteEntryResponseDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
}

public sealed class ReVectorizeResponseDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("vectorized")] public bool Vectorized { get; set; }
}

// ===== 列表 =====

public sealed class KnowledgeEntryListItemDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }
    // doc_id / created_by 即便为 null 也要输出
    [JsonPropertyName("doc_id"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? DocId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("created_by"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CreatedBy { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("vectorized")] public bool Vectorized { get; set; }
}

public sealed class KnowledgeListResponseDto
{
    [JsonPropertyName("items")] public List<KnowledgeEntryListItemDto> Items { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

// ===== #18 导入 =====

public sealed class ImportPreviewResponseDto
{
    [JsonPropertyName("job_id")] public long JobId { get; set; }
    [JsonPropertyName("job_type")] public string JobType { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("total_rows")] public int TotalRows { get; set; }
    [JsonPropertyName("valid_rows")] public int ValidRows { get; set; }
    [JsonPropertyName("dup_rows")] public int DupRows { get; set; }
    [JsonPropertyName("error_rows")] public int ErrorRows { get; set; }
    [JsonPropertyName("errors")] public List<ImportErrorDto> Errors { get; set; } = new();
    [JsonPropertyName("status")] public string Status { get; set; } = "previewing";
}

public sealed class ImportErrorDto
{
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("field")] public string Field { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

public sealed class ConfirmImportRequestDto
{
    [JsonPropertyName("dup_strategy")] public string DupStrategy { get; set; } = "skip";
}

public sealed class ConfirmImportResponseDto
{
    [JsonPropertyName("job_id")] public long JobId { get; set; }
    [JsonPropertyName("started")] public bool Started { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

public sealed class ImportJobDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("job_type")] public string JobType { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("total_rows")] public int TotalRows { get; set; }
    [JsonPropertyName("valid_rows")] public int ValidRows { get; set; }
    [JsonPropertyName("dup_rows")] public int DupRows { get; set; }
    [JsonPropertyName("error_rows")] public int ErrorRows { get; set; }
    [JsonPropertyName("imported_rows")] public int ImportedRows { get; set; }
    [JsonPropertyName("vectorized")] public int Vectorized { get; set; }
    [JsonPropertyName("dup_strategy")] public string DupStrategy { get; set; } = "skip";
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class ImportJobsListResponseDto
{
    [JsonPropertyName("items")] public List<ImportJobDto> Items { get; set; } = new();
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

// ===== #20 文档 =====

public sealed class UploadDocumentRequestDto
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("doc_type")] public string DocType { get; set; } = "manual";
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model_scope")] public string[]? ModelScope { get; set; }
    [JsonPropertyName("source_file")] public string? SourceFile { get; set; }
    [JsonPropertyName("page_count")] public int PageCount { get; set; }
    [JsonPropertyName("chunks")] public List<UploadChunkDto>? Chunks { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
}

public sealed class UploadChunkDto
{
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("heading_path")] public string? HeadingPath { get; set; }
    [JsonPropertyName("page_from")] public int? PageFrom { get; set; }
    [JsonPropertyName("page_to")] public int? PageTo { get; set; }
}

public sealed class UploadDocumentResponseDto
{
    [JsonPropertyName("doc_id")] public long DocId { get; set; }
    [JsonPropertyName("chunk_ids")] public List<long> ChunkIds { get; set; } = new();
}

public sealed class DocumentDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("doc_type")] public string DocType { get; set; } = "";
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("page_count")] public int PageCount { get; set; }
    [JsonPropertyName("error_msg")] public string? ErrorMsg { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class DocumentListResponseDto
{
    [JsonPropertyName("items")] public List<DocumentDto> Items { get; set; } = new();
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

public sealed class ChunkDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("doc_id")] public long DocId { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("heading_path")] public string? HeadingPath { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("content_len")] public int ContentLen { get; set; }
    [JsonPropertyName("page_from")] public int? PageFrom { get; set; }
    [JsonPropertyName("page_to")] public int? PageTo { get; set; }
    [JsonPropertyName("has_vector")] public bool HasVector { get; set; }
}

public sealed class ChunkListResponseDto
{
    [JsonPropertyName("items")] public List<ChunkDto> Items { get; set; } = new();
}

public sealed class DeleteDocumentResponseDto
{
    [JsonPropertyName("doc_id")] public long DocId { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
}
