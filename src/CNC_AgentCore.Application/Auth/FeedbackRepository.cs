// Application/Auth/FeedbackRepository.cs —— log.feedbacks 仓储（Dapper）。
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Auth;

public sealed class FeedbackRepository : IFeedbackRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public FeedbackRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<long> InsertAsync(FeedbackRecord r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO log.feedbacks (
                query_log_id, trace_id, user_code, verdict, reason, bad_refs, comment, correction
            ) VALUES (
                @queryLogId, @traceId, @userCode, @verdict, @reason, @badRefs, @comment, @correction
            )
            RETURNING id
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("queryLogId", r.QueryLogId);
        p.Add("traceId", r.TraceId);
        p.Add("userCode", r.UserCode);
        p.Add("verdict", r.Verdict);
        p.Add("reason", r.Reason);
        p.Add("badRefs", r.BadRefs ?? Array.Empty<int>());
        p.Add("comment", r.Comment);
        p.Add("correction", r.Correction);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    public async Task<bool> UpdateQueryLogFeedbackAsync(Guid traceId, int verdict, string? note, CancellationToken ct = default)
    {
        const string sql = "UPDATE log.query_logs SET feedback = @verdict, feedback_note = @note WHERE trace_id = @traceId";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { traceId, verdict, note }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<int?> GetLatestVerdictByTraceAsync(Guid traceId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT verdict FROM log.feedbacks
             WHERE trace_id = @traceId
             ORDER BY id DESC
             LIMIT 1
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { traceId }, cancellationToken: ct));
    }
}
