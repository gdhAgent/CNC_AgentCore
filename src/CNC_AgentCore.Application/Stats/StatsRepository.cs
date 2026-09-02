// Application/Stats/StatsRepository.cs —— 高频故障 Top-N 聚合。
// 双源：查询侧 log.query_logs.detected_codes（LATERAL unnest）；工单侧 ops.maintenance_logs + joins。
// 时间字段：查询侧 created_at、工单侧 started_at。
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Stats;

public sealed class StatsRepository : IStatsRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public StatsRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    private const string QueryAggSql = """
        SELECT code_norm, COUNT(*) AS cnt, MAX(ql.created_at) AS last_seen
          FROM log.query_logs ql,
               LATERAL unnest(ql.detected_codes) AS code_norm
         {where}
         GROUP BY code_norm
         ORDER BY cnt DESC, code_norm ASC
         LIMIT @topN
        """;

    private const string MaintAggSql = """
        SELECT ml.alarm_code AS CodeNorm,
               COUNT(*) AS cnt,
               MAX(ml.started_at) AS LastSeen,
               MAX(a.name) AS AlarmName,
               MAX(a.severity) AS severity,
               MAX(m.brand) AS brand
          FROM ops.maintenance_logs ml
          LEFT JOIN ops.machines m ON m.id = ml.machine_id
          LEFT JOIN kb.alarms a
            ON a.code_norm = ml.alarm_code AND a.brand = m.brand
         {where}
           AND ml.alarm_code IS NOT NULL
         GROUP BY ml.alarm_code
         ORDER BY cnt DESC, ml.alarm_code ASC
         LIMIT @topN
        """;

    private const string EnrichSql = """
        SELECT code_norm, name, severity, brand
          FROM kb.alarms
         WHERE code_norm = ANY(@codes)
        """;

    public async Task<(List<TopFaultAggItem> Items, long Total)> FetchTopByQueryAsync(
        DateTimeOffset? fromTime, DateTimeOffset? toTime, int topN, CancellationToken ct = default)
    {
        var (where, p) = BuildWindow("created_at", fromTime, toTime, topN);
        var totalSql = $"SELECT count(*) FROM log.query_logs{where}";
        var aggSql = QueryAggSql.Replace("{where}", where);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(totalSql, p, cancellationToken: ct));
        var rows = await conn.QueryAsync<(string CodeNorm, long Cnt, DateTimeOffset LastSeen)>(
            new CommandDefinition(aggSql, p, cancellationToken: ct));
        var items = rows.Select(r => new TopFaultAggItem(r.CodeNorm, (int)r.Cnt, r.LastSeen)).ToList();
        return (items, total);
    }

    public async Task<(List<TopFaultAggItem> Items, long Total)> FetchTopByMaintenanceAsync(
        DateTimeOffset? fromTime, DateTimeOffset? toTime, int topN, CancellationToken ct = default)
    {
        var (where, p) = BuildWindow("started_at", fromTime, toTime, topN);
        var totalSql = $"SELECT count(*) FROM ops.maintenance_logs{where}";
        var aggSql = MaintAggSql.Replace("{where}", where);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(totalSql, p, cancellationToken: ct));
        var rows = await conn.QueryAsync<MaintAggRow>(
            new CommandDefinition(aggSql, p, cancellationToken: ct));
        var items = rows.Select(r => new TopFaultAggItem(
            r.CodeNorm ?? string.Empty, (int)r.Cnt, r.LastSeen, r.AlarmName, r.Severity, r.Brand)).ToList();
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<string, CodeEnrichMeta>> EnrichCodesAsync(
        IReadOnlyCollection<string> codeNorms, CancellationToken ct = default)
    {
        if (codeNorms.Count == 0)
            return new Dictionary<string, CodeEnrichMeta>();

        var arr = codeNorms.ToArray();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(string CodeNorm, string Name, string? Severity, string? Brand)>(
            new CommandDefinition(EnrichSql, new { codes = arr }, cancellationToken: ct));

        var dict = new Dictionary<string, CodeEnrichMeta>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            // 同一 code_norm 可能多个 brand（不同厂商等价码），只取第一条
            if (!dict.ContainsKey(r.CodeNorm))
                dict[r.CodeNorm] = new CodeEnrichMeta(r.Name, r.Severity, r.Brand);
        }
        return dict;
    }

    /// <summary>构造时间窗口 WHERE 与参数；col 白名单 created_at/started_at（内部硬编码，不接外部输入）。</summary>
    private static (string Where, DynamicParameters Params) BuildWindow(
        string col, DateTimeOffset? fromTime, DateTimeOffset? toTime, int topN)
    {
        var clauses = new List<string>();
        var p = new DynamicParameters();
        if (fromTime is not null) { clauses.Add($"{col} >= @fromTime"); p.Add("fromTime", fromTime.Value); }
        if (toTime is not null) { clauses.Add($"{col} <= @toTime"); p.Add("toTime", toTime.Value); }
        p.Add("topN", topN);
        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        return (where, p);
    }

    private sealed class MaintAggRow
    {
        public string? CodeNorm { get; set; }
        public long Cnt { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
        public string? AlarmName { get; set; }
        public string? Severity { get; set; }
        public string? Brand { get; set; }
    }
}
