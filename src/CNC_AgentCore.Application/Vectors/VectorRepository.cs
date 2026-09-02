// Application/Vectors/VectorRepository.cs —— 向量总览 + 无向量清单（Dapper）。
// 命名空间用 Vectors 而非 Vector：与 Pgvector.Vector 类型撞名。
using System.Globalization;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Vectors;

public sealed class VectorRepository : IVectorRepository
{
    private readonly NpgsqlDataSource _dataSource;

    // 三表固定元数据
    private static readonly Dictionary<string, (string Label, string Note, bool DesignedSkip)> TableMeta = new()
    {
        ["alarms"] = ("报警码", "kb.alarms：码+名称+现象+原因+处置 拼文本后嵌入", false),
        ["chunks"] = ("知识块", "kb.chunks：只给子块（level=2）向量化，父块仅做上下文", true),
        ["maintenance_logs"] = ("维修工单", "ops.maintenance_logs：现象+处置 嵌入（相似历史故障检索）", false),
    };

    // chunks 用 FILTER (WHERE level = 2) —— 父块按设计不向量化，不计入"缺"的口径。
    private const string ChunksSql = """
        SELECT count(*) FILTER (WHERE level = 2) AS total,
               count(embedding) FILTER (WHERE level = 2) AS WithEmb,
               min(vector_dims(embedding)) FILTER (WHERE level = 2) AS DimMin,
               max(vector_dims(embedding)) FILTER (WHERE level = 2) AS DimMax
          FROM kb.chunks
        """;

    private const string AlarmsSql = """
        SELECT count(*) AS total,
               count(embedding) AS WithEmb,
               min(vector_dims(embedding)) AS DimMin,
               max(vector_dims(embedding)) AS DimMax
          FROM kb.alarms
        """;

    private const string MaintenanceLogsSql = """
        SELECT count(*) AS total,
               count(embedding) AS WithEmb,
               min(vector_dims(embedding)) AS DimMin,
               max(vector_dims(embedding)) AS DimMax
          FROM ops.maintenance_logs
        """;

    public VectorRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    private sealed class CountRow
    {
        public long Total { get; set; }
        public long WithEmb { get; set; }
        public int? DimMin { get; set; }
        public int? DimMax { get; set; }
    }

    public async Task<List<VectorTableStat>> GetOverviewAsync(CancellationToken ct = default)
    {
        // 三表并行 fetch
        var alarmsTask = FetchTableAsync(AlarmsSql, ct);
        var chunksTask = FetchTableAsync(ChunksSql, ct);
        var mlTask = FetchTableAsync(MaintenanceLogsSql, ct);
        await Task.WhenAll(alarmsTask, chunksTask, mlTask);

        return new List<VectorTableStat>
        {
            ToStat("alarms", alarmsTask.Result),
            ToStat("chunks", chunksTask.Result),
            ToStat("maintenance_logs", mlTask.Result),
        };
    }

    private async Task<CountRow> FetchTableAsync(string sql, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.QueryFirstAsync<CountRow>(
            new CommandDefinition(sql, cancellationToken: ct));
    }

    private static VectorTableStat ToStat(string key, CountRow r)
    {
        var meta = TableMeta[key];
        var withEmb = r.WithEmb;
        return new VectorTableStat(
            Table: key,
            Label: meta.Label,
            Note: meta.Note,
            DesignedSkip: meta.DesignedSkip,
            Total: r.Total,
            WithEmbedding: withEmb,
            Without: r.Total - withEmb,
            DimMin: r.DimMin,
            DimMax: r.DimMax);
    }

    // ===== 无向量清单 =====

    /// <summary>行模型。可写 row 类 + 投影：直接物化 record 会因 smallint/缺省列与 ctor 类型失配抛异常。</summary>
    private sealed class UnvecRow
    {
        public long Id { get; set; }
        public string? Code { get; set; }
        public int? Level { get; set; }
        public string Title { get; set; } = "";
        public string? Detail { get; set; }
    }

    private static UnvectorizedItem ToUnvec(UnvecRow r) => new(
        r.Id, r.Code, r.Level, r.Title, r.Detail);

    public async Task<(List<UnvectorizedItem> Items, int Total)> ListUnvectorizedAsync(
        string table, int limit, int offset, CancellationToken ct = default)
    {
        // 三表字段不齐（chunks 用 level、alarms/mlogs 用 code）：统一补 NULL 列输出 id/code/level/title/detail。
        // chunks 强制 level=2：父块按设计不向量化，不入缺向量清单。
        string baseSql = table switch
        {
            "alarms" =>
                "SELECT id, code_norm AS code, NULL::int AS level, name AS title, NULL::text AS detail " +
                "FROM kb.alarms WHERE embedding IS NULL",
            "chunks" =>
                "SELECT c.id, NULL::text AS code, c.level, " +
                "       COALESCE(c.heading_path, d.title) AS title, " +
                "       left(c.content, 80) AS detail " +
                "FROM kb.chunks c JOIN kb.documents d ON d.id = c.doc_id " +
                "WHERE c.embedding IS NULL AND c.level = 2",
            "maintenance_logs" =>
                "SELECT id, COALESCE(order_no, '无工单号') AS code, NULL::int AS level, " +
                "       left(symptom, 80) AS title, NULL::text AS detail " +
                "FROM ops.maintenance_logs WHERE embedding IS NULL",
            _ => throw new ArgumentException($"table 必须为 alarms/chunks/maintenance_logs 之一（实际={table}）"),
        };

        var countSql = $"SELECT count(*) FROM ({baseSql}) s";
        var listSql = $"{baseSql} ORDER BY id LIMIT @lim OFFSET @off";

        var p = new DynamicParameters();
        p.Add("lim", limit);
        p.Add("off", offset);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, p, cancellationToken: ct));
        var rows = await conn.QueryAsync<UnvecRow>(new CommandDefinition(listSql, p, cancellationToken: ct));
        return (rows.Select(ToUnvec).ToList(), total);
    }

    // ===== embedding-map 数据源：本方法返 raw embedding，端点经 VectorPca 降维 =====

    /// <summary>每张表允许的 group_by 字段（公开，Endpoint 用）。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> GroupByOptions =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["alarms"] = new[] { "category", "brand", "severity" },
            ["chunks"] = new[] { "doc", "level" },
            ["maintenance_logs"] = new[] { "fault_type", "brand" },
        };

    /// <summary>每张表默认 group_by（公开）。</summary>
    public static readonly IReadOnlyDictionary<string, string> GroupByDefault =
        new Dictionary<string, string>
        {
            ["alarms"] = "category",
            ["chunks"] = "doc",
            ["maintenance_logs"] = "fault_type",
        };

    // 表 → (id_expr, label_expr, group_sql[by_field], from_sql, where_sql)
    private static readonly Dictionary<string, (string IdExpr, string LabelExpr,
        Dictionary<string, string> GroupSql, string FromSql, string WhereSql)> EmbeddingMapSql = new()
    {
        ["alarms"] = (
            "a.id",
            "(a.code_norm || ' · ' || a.name)",
            new Dictionary<string, string>
            {
                ["category"] = "a.category",
                ["brand"] = "a.brand",
                ["severity"] = "a.severity",
            },
            "FROM kb.alarms a",
            "a.embedding IS NOT NULL"),
        ["chunks"] = (
            "c.id",
            "COALESCE(c.heading_path, d.title, '知识块')",
            new Dictionary<string, string>
            {
                ["doc"] = "COALESCE(d.title, '未命名文档')",
                ["level"] = "('level ' || c.level)",
            },
            "FROM kb.chunks c LEFT JOIN kb.documents d ON d.id = c.doc_id",
            "c.embedding IS NOT NULL"),
        ["maintenance_logs"] = (
            "ml.id",
            "(COALESCE(ml.order_no, '无单号') || ' · ' || left(ml.symptom, 40))",
            new Dictionary<string, string>
            {
                ["fault_type"] = "COALESCE(ml.fault_type, '未分类')",
                ["brand"] = "COALESCE(m.brand, '未知品牌')",
            },
            "FROM ops.maintenance_logs ml LEFT JOIN ops.machines m ON m.id = ml.machine_id",
            "ml.embedding IS NOT NULL"),
    };

    public async Task<List<EmbeddingMapItem>> FetchEmbeddingMapAsync(
        string table, string groupBy, int limit, CancellationToken ct = default)
    {
        if (!EmbeddingMapSql.TryGetValue(table, out var sqlMeta))
            throw new ArgumentException($"table 必须为 {string.Join('/', EmbeddingMapSql.Keys)} 之一");
        if (!sqlMeta.GroupSql.TryGetValue(groupBy, out var groupExpr))
            throw new ArgumentException($"table={table} 的 group_by 必须为 {string.Join('/', sqlMeta.GroupSql.Keys)} 之一");

        // embedding::text 输出 "[a,b,c,...]"，Dapper 读成 string 后本地解析
        var sql = $$"""
            SELECT {{sqlMeta.IdExpr}} AS id,
                   {{sqlMeta.LabelExpr}} AS label,
                   {{groupExpr}} AS grp,
                   embedding::text AS vec_text
              {{sqlMeta.FromSql}}
             WHERE {{sqlMeta.WhereSql}}
             ORDER BY {{sqlMeta.IdExpr}}
             LIMIT @lim
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long Id, string Label, string Group, string VecText)>(
            new CommandDefinition(sql, new { lim = limit }, cancellationToken: ct));

        var items = new List<EmbeddingMapItem>();
        foreach (var r in rows)
        {
            var vec = ParseVectorLiteral(r.VecText);
            if (vec is null) continue;
            items.Add(new EmbeddingMapItem(r.Id, r.Label, r.Group, vec));
        }
        return items;
    }

    /// <summary>pgvector ::text 输出格式 "[a,b,c,...]" → float[]。解析失败返回 null。</summary>
    private static float[]? ParseVectorLiteral(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.Length < 2 || text[0] != '[' || text[^1] != ']') return null;
        var inner = text[1..^1];
        if (string.IsNullOrWhiteSpace(inner)) return Array.Empty<float>();
        try
        {
            return inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture))
                .ToArray();
        }
        catch
        {
            return null;
        }
    }
}
