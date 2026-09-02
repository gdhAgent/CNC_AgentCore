// Application/Vectorizer/VectorizeService.cs —— 后台补跑缺失向量。
// 命名空间用 Vectorizer 而非 Vector：避开与 Pgvector.Vector 类型撞名。
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Vectorizer;

public sealed class VectorizeService : IVectorizeService
{
    private readonly NpgsqlDataSource _ds;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _emb;
    private readonly ILogger<VectorizeService> _log;

    // 表 → schema 映射（maintenance_logs 在 ops；alarms/chunks 在 kb）
    private static readonly Dictionary<string, string> SchemaMap = new()
    {
        ["alarms"] = "kb",
        ["chunks"] = "kb",
        ["maintenance_logs"] = "ops",
    };

    public VectorizeService(
        NpgsqlDataSource ds,
        IEmbeddingGenerator<string, Embedding<float>> emb,
        ILogger<VectorizeService> log)
    {
        _ds = ds;
        _emb = emb;
        _log = log;
    }

    // ===== 三表 fetch SQL =====
    private const string AlarmsSql = """
        SELECT id, brand, controller, code, name, description, cause, action, safety_note AS SafetyNote
          FROM kb.alarms WHERE embedding IS NULL ORDER BY id
        """;
    private const string ChunksSql = """
        SELECT id, content, COALESCE(heading_path, '') AS HeadingPath
          FROM kb.chunks WHERE level = 2 AND embedding IS NULL ORDER BY id
        """;
    private const string MaintenanceLogsSql = """
        SELECT ml.id, ml.alarm_code AS AlarmCode, ml.fault_type AS FaultType, ml.symptom, ml.action_taken AS ActionTaken,
               m.asset_no AS AssetNo, m.brand, m.model, m.controller
          FROM ops.maintenance_logs ml
          JOIN ops.machines m ON m.id = ml.machine_id
         WHERE ml.embedding IS NULL ORDER BY ml.id
        """;

    // ===== 三表 row 类：可写类 + SQL 单 token 别名（record 直映蛇形列/无默认 ctor 会抛异常）=====
    private sealed class AlarmRow
    {
        public long Id { get; set; }
        public string Brand { get; set; } = "";
        public string? Controller { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Cause { get; set; }
        public string? Action { get; set; }
        public string? SafetyNote { get; set; }
    }

    private sealed class ChunkRow
    {
        public long Id { get; set; }
        public string? Content { get; set; }
        public string HeadingPath { get; set; } = "";
    }

    private sealed class MaintenanceLogRow
    {
        public long Id { get; set; }
        public string? AlarmCode { get; set; }
        public string? FaultType { get; set; }
        public string? Symptom { get; set; }
        public string? ActionTaken { get; set; }
        public string AssetNo { get; set; } = "";
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? Controller { get; set; }
    }

    public async Task<VectorizeResult> RunAsync(string table, int batch, CancellationToken ct = default)
    {
        if (!SchemaMap.TryGetValue(table, out var schema))
            throw new ArgumentException($"table 必须为 {string.Join('/', SchemaMap.Keys)} 之一");

        var sw = Stopwatch.StartNew();
        var embedded = 0;
        var failed = 0;
        var skipped = 0;

        // 1) fetch + text 构造（一次性，同连接）
        var items = new List<(long Id, string Text)>();
        await using (var conn = await _ds.OpenConnectionAsync(ct))
        {
            switch (table)
            {
                case "alarms":
                    foreach (var r in await conn.QueryAsync<AlarmRow>(new CommandDefinition(AlarmsSql, cancellationToken: ct)))
                    {
                        var text = BuildAlarmText(r);
                        if (string.IsNullOrWhiteSpace(text)) skipped++;
                        else items.Add((r.Id, text));
                    }
                    break;
                case "chunks":
                    foreach (var r in await conn.QueryAsync<ChunkRow>(new CommandDefinition(ChunksSql, cancellationToken: ct)))
                    {
                        var text = BuildChunkText(r);
                        if (string.IsNullOrWhiteSpace(text)) skipped++;
                        else items.Add((r.Id, text));
                    }
                    break;
                case "maintenance_logs":
                    foreach (var r in await conn.QueryAsync<MaintenanceLogRow>(new CommandDefinition(MaintenanceLogsSql, cancellationToken: ct)))
                    {
                        var text = BuildMaintenanceLogText(r);
                        if (string.IsNullOrWhiteSpace(text)) skipped++;
                        else items.Add((r.Id, text));
                    }
                    break;
            }

            // 2) 分批 embed + write
            for (var i = 0; i < items.Count; i += batch)
            {
                var batchItems = items.Skip(i).Take(batch).ToList();
                if (batchItems.Count == 0) continue;
                var ids = batchItems.Select(x => x.Id).ToList();
                var texts = batchItems.Select(x => x.Text).ToList();

                try
                {
                    var vectors = await EmbedWithRetryAsync(texts, ct);
                    await WriteEmbeddingsAsync(conn, schema, table, ids, vectors, ct);
                    embedded += batchItems.Count;
                    _log.LogInformation("[OK] {Table} batch {Batch} (ids {First}..{Last})",
                        table, (i / batch) + 1, ids[0], ids[^1]);
                }
                catch (Exception ex)
                {
                    failed += batchItems.Count;
                    _log.LogWarning(ex, "[FAIL] {Table} batch {Batch}", table, (i / batch) + 1);
                }
            }
        }

        var elapsed = (int)sw.ElapsedMilliseconds;
        _log.LogInformation("[vectors] 补跑完成 table={Table} embedded={Embedded} failed={Failed} elapsed_ms={Elapsed}",
            table, embedded, failed, elapsed);
        return new VectorizeResult(table, items.Count + skipped, embedded, failed, skipped, elapsed);
    }

    // ===== 文本构造（与入库/单条向量化共用同一拼装）=====

    private static string BuildAlarmText(AlarmRow r)
    {
        var head = $"[{r.Brand}][{r.Controller ?? ""}] 报警{r.Code} {r.Name}".Trim();
        var parts = new List<string> { head + "。" };
        if (!string.IsNullOrEmpty(r.Description)) parts.Add($"现象：{r.Description}");
        if (!string.IsNullOrEmpty(r.Cause)) parts.Add($"原因：\n{r.Cause}");
        if (!string.IsNullOrEmpty(r.Action)) parts.Add($"处置：\n{r.Action}");
        return string.Join("\n", parts);
    }

    private static string BuildChunkText(ChunkRow r)
    {
        var heading = r.HeadingPath ?? "";
        var content = r.Content ?? "";
        if (!string.IsNullOrEmpty(heading))
            return $"{heading}\n{content}".Trim();
        return content.Trim();
    }

    private static string BuildMaintenanceLogText(MaintenanceLogRow r)
    {
        var assetNo = r.AssetNo ?? "?";
        var alarmCode = r.AlarmCode ?? "无";
        var faultType = r.FaultType ?? "";
        var symptom = (r.Symptom ?? "").Trim();
        var action = (r.ActionTaken ?? "").Trim();
        var parts = new List<string> { $"[{assetNo}][{alarmCode}]" };
        if (!string.IsNullOrEmpty(faultType)) parts.Add($"[{faultType}]");
        parts.Add(symptom);
        if (!string.IsNullOrEmpty(action)) parts.Add($"处置：{action}");
        return string.Join("\n", parts);
    }

    // ===== 嵌入（3 次重试 + 指数退避）=====

    private async Task<List<float[]>> EmbedWithRetryAsync(List<string> texts, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var result = await _emb.GenerateAsync(texts, cancellationToken: ct);
                return result.Select(e => e.Vector.ToArray()).ToList();
            }
            catch (Exception) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }
        // 末次失败直接抛
        var lastResult = await _emb.GenerateAsync(texts, cancellationToken: ct);
        return lastResult.Select(e => e.Vector.ToArray()).ToList();
    }

    // ===== 写回（逐条 UPDATE + ::vector 强转）=====

    private static async Task WriteEmbeddingsAsync(
        NpgsqlConnection conn, string schema, string table, List<long> ids, List<float[]> vectors, CancellationToken ct)
    {
        var sql = $"UPDATE {schema}.{table} SET embedding = @vec::vector WHERE id = @id";
        for (var i = 0; i < ids.Count; i++)
        {
            var vecLiteral = "[" + string.Join(",", vectors[i].Select(v => v.ToString("G9"))) + "]";
            await conn.ExecuteAsync(new CommandDefinition(
                sql, new { id = ids[i], vec = vecLiteral }, cancellationToken: ct));
        }
    }
}
