// Application/Auth/QueryLogRepository.cs —— log 层查询日志落库与列表（Dapper）。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Auth;

public sealed class QueryLogRepository : IQueryLogRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public QueryLogRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<long> InsertAsync(QueryLogRecord r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO log.query_logs (
                trace_id, session_id, user_code, raw_query, detected_codes,
                route, tool_calls, retrieved, top_score, answer, refused,
                latency_ms, latency_breakdown, prompt_tokens, completion_tokens
            ) VALUES (
                @traceId, @sessionId, @userCode, @rawQuery, @detectedCodes,
                @route, @toolCalls::jsonb, @retrieved::jsonb, @topScore, @answer, @refused,
                @latencyMs, @latencyBreakdown::jsonb, @promptTokens, @completionTokens
            )
            RETURNING id
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("traceId", r.TraceId);
        p.Add("sessionId", r.SessionId);
        p.Add("userCode", r.UserCode);
        p.Add("rawQuery", r.RawQuery);
        p.Add("detectedCodes", r.DetectedCodes.ToArray());
        p.Add("route", r.Route);
        p.Add("toolCalls", r.ToolCalls is null ? null : JsonSerializer.Serialize(r.ToolCalls));
        p.Add("retrieved", JsonSerializer.Serialize(r.RetrievedSnapshot));
        p.Add("topScore", r.TopScore);
        p.Add("answer", r.Answer);
        p.Add("refused", r.Refused);
        p.Add("latencyMs", r.LatencyMs);
        p.Add("latencyBreakdown", JsonSerializer.Serialize(r.LatencyBreakdown));
        p.Add("promptTokens", r.PromptTokens);
        p.Add("completionTokens", r.CompletionTokens);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    public async Task<int> InsertTraceStepsAsync(
        long queryLogId, Guid traceId, IReadOnlyList<Dictionary<string, object?>> steps, CancellationToken ct = default)
    {
        if (steps.Count == 0) return 0;
        const string sql = """
            INSERT INTO log.query_trace_steps (query_log_id, trace_id, seq, step, status, started_at, ms, input, output, note)
            VALUES (@qid, @traceId, @seq, @step, @status, @startedAt, @ms, @input::jsonb, @output::jsonb, @note)
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var seq = 0;
        foreach (var s in steps)
        {
            seq++;
            var startedAt = s.TryGetValue("started_at", out var sa) && DateTimeOffset.TryParse(sa?.ToString(), out var dto)
                ? dto
                : DateTimeOffset.UtcNow;
            var p = new DynamicParameters();
            p.Add("qid", queryLogId);
            p.Add("traceId", traceId);
            p.Add("seq", seq);
            p.Add("step", GetStr(s, "step"));
            p.Add("status", GetStr(s, "status", "ok"));
            p.Add("startedAt", startedAt);
            p.Add("ms", GetInt(s, "ms"));
            p.Add("input", JsonSerializer.Serialize(s.TryGetValue("input", out var i) ? i : new Dictionary<string, object?>()));
            p.Add("output", JsonSerializer.Serialize(s.TryGetValue("output", out var o) ? o : new Dictionary<string, object?>()));
            p.Add("note", s.TryGetValue("note", out var n) ? n?.ToString() : null);
            await conn.ExecuteAsync(new CommandDefinition(sql, p, transaction: tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
        return seq;
    }

    private static string GetStr(Dictionary<string, object?> d, string key, string fallback = "")
        => d.TryGetValue(key, out var v) ? v?.ToString() ?? fallback : fallback;

    private static int GetInt(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is int i ? i : 0;

    public async Task<QueryLogInfo?> GetInfoByTraceAsync(Guid traceId, CancellationToken ct = default)
    {
        const string sql = "SELECT id, raw_query, detected_codes FROM log.query_logs WHERE trace_id = @traceId";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<(long Id, string RawQuery, string[] DetectedCodes)>(
            new CommandDefinition(sql, new { traceId }, cancellationToken: ct));
        if (row.Id == 0 && row.RawQuery is null) return null;
        return new QueryLogInfo(row.Id, row.RawQuery, row.DetectedCodes?.ToList() ?? new List<string>());
    }

    private sealed class FullDetailRow
    {
        public long Id { get; set; }
        public Guid TraceId { get; set; }
        public string RawQuery { get; set; } = "";
        public string Route { get; set; } = "";
        public bool Refused { get; set; }
        public string[] DetectedCodes { get; set; } = Array.Empty<string>();
        public string? Answer { get; set; }
        public int? LatencyMs { get; set; }
        public string LatencyBreakdown { get; set; } = "{}";        // JSONB ::text
        public string ToolCalls { get; set; } = "[]";               // JSONB ::text
        public DateTimeOffset CreatedAt { get; set; }
    }

    public async Task<QueryLogFullDetail?> GetFullDetailAsync(Guid traceId, CancellationToken ct = default)
    {
        // JSONB 列 ::text 强转后 Dapper 映射到 string；API 层按需反序列化
        const string sql = """
            SELECT id AS Id, trace_id AS TraceId, raw_query AS RawQuery,
                   route AS Route, refused AS Refused, detected_codes AS DetectedCodes,
                   answer AS Answer, latency_ms AS LatencyMs,
                   latency_breakdown::text AS LatencyBreakdown,
                   tool_calls::text AS ToolCalls,
                   created_at AS CreatedAt
              FROM log.query_logs
             WHERE trace_id = @traceId
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<FullDetailRow>(
            new CommandDefinition(sql, new { traceId }, cancellationToken: ct));
        if (row is null) return null;

        return new QueryLogFullDetail(
            row.Id, row.TraceId, row.RawQuery, row.Route, row.Refused,
            row.DetectedCodes.ToList(), row.Answer, row.LatencyMs,
            ParseDictInt(row.LatencyBreakdown),
            ParseListDictObj(row.ToolCalls),
            row.CreatedAt);
    }

    public async Task<List<TraceStepRow>> GetTraceStepsAsync(Guid traceId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT seq AS Seq, step AS Step, status AS Status,
                   started_at AS StartedAt, ms AS Ms,
                   input::text AS Input,
                   output::text AS Output,
                   note AS Note
              FROM log.query_trace_steps
             WHERE trace_id = @traceId
             ORDER BY seq
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(int Seq, string Step, string Status, DateTimeOffset StartedAt, int Ms, string Input, string Output, string? Note)>(
            new CommandDefinition(sql, new { traceId }, cancellationToken: ct));
        return rows.Select(r => new TraceStepRow(
            r.Seq, r.Step, r.Status, r.StartedAt, r.Ms,
            ParseDictObj(r.Input), ParseDictObj(r.Output), r.Note)).ToList();
    }

    private static Dictionary<string, int> ParseDictInt(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static Dictionary<string, object?> ParseDictObj(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static List<Dictionary<string, object?>> ParseListDictObj(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    // ===== 日志列表 =====

    /// <summary>日志列表行模型。可写 row 类 + 单 token 别名 + 投影：
    /// Dapper 直接物化 record 会因 smallint/timestamptz 与 ctor 参数类型失配抛异常。</summary>
    private sealed class LogRow
    {
        public long Id { get; set; }
        public Guid TraceId { get; set; }
        public string RawQuery { get; set; } = "";
        public string Route { get; set; } = "";
        public bool Refused { get; set; }
        public short? Feedback { get; set; }
        public int? LatencyMs { get; set; }
        public string? UserCode { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }

    private static QueryLogListItem ToLog(LogRow r) => new(
        r.Id, r.TraceId, r.RawQuery, r.Route, r.Refused,
        r.Feedback is null ? null : (int)r.Feedback,
        r.LatencyMs, r.UserCode, r.CreatedAt);

    public async Task<(List<QueryLogListItem> Items, int Total)> ListAsync(QueryLogListQuery q, CancellationToken ct = default)
    {
        // IS NULL OR 短路避免动态 SQL 拼接。feedback 复合：FeedbackAny=true 只看有无反馈；否则按反馈值匹配或不限。
        const string filterClause = """
             WHERE (@refused::boolean IS NULL OR refused = @refused)
               AND (
                   (@feedbackAny::boolean = true AND feedback IS NOT NULL)
                OR (@feedbackAny::boolean = false AND (@feedback::int IS NULL OR feedback = @feedback::int))
               )
               AND (@route::text IS NULL OR route = @route)
               AND (@userCode::text IS NULL OR user_code = @userCode)
               AND (@q::text IS NULL OR raw_query ILIKE '%' || @q || '%')
               AND (@fromTime::timestamptz IS NULL OR created_at >= @fromTime)
               AND (@toTime::timestamptz IS NULL OR created_at <= @toTime)
            """;
        var listSql = $$"""
            SELECT id AS Id, trace_id AS TraceId, raw_query AS RawQuery,
                   route AS Route, refused AS Refused, feedback AS Feedback,
                   latency_ms AS LatencyMs, user_code AS UserCode,
                   created_at AS CreatedAt
              FROM log.query_logs
            {{filterClause}}
             ORDER BY id DESC
             LIMIT @limit OFFSET @offset
            """;
        var countSql = $"SELECT count(*) FROM log.query_logs{filterClause}";

        var p = new DynamicParameters();
        p.Add("refused", q.Refused);
        p.Add("feedbackAny", q.FeedbackAny);
        p.Add("feedback", q.Feedback);
        p.Add("route", q.Route);
        p.Add("userCode", q.UserCode);
        p.Add("q", q.Q);
        p.Add("fromTime", q.FromTime);
        p.Add("toTime", q.ToTime);
        p.Add("limit", q.Limit);
        p.Add("offset", q.Offset);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, p, cancellationToken: ct));
        var rows = await conn.QueryAsync<LogRow>(new CommandDefinition(listSql, p, cancellationToken: ct));
        return (rows.Select(ToLog).ToList(), total);
    }
}
