// Application/Retrieval/VectorSearch.cs —— 向量召回（pgvector cosine）
using System.Diagnostics;
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;
using Dapper;
using Pgvector;

namespace CNC_AgentCore.Application.Retrieval;

public sealed class VectorSearch
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;

    public VectorSearch(Npgsql.NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string ChunksSql = """
        WITH q AS (SELECT @vec::vector AS vec)
        SELECT 'chunk'::text AS type,
               c.id AS id,
               1 - (c.embedding <=> q.vec) AS score,
               ROW_NUMBER() OVER (ORDER BY c.embedding <=> q.vec) AS rank,
               c.heading_path AS title,
               COALESCE(d.title, '') || COALESCE(' P' || c.page_from, '') AS source,
               LEFT(c.content, @preview) AS content
          FROM kb.chunks c
          CROSS JOIN q
          LEFT JOIN kb.documents d ON d.id = c.doc_id
         WHERE c.level = 2 AND c.embedding IS NOT NULL
         {BRAND_FILTER}
         ORDER BY c.embedding <=> q.vec
         LIMIT @topN
        """;

    private const string AlarmsSql = """
        WITH q AS (SELECT @vec::vector AS vec)
        SELECT 'alarm'::text AS type,
               a.id AS id,
               1 - (a.embedding <=> q.vec) AS score,
               ROW_NUMBER() OVER (ORDER BY a.embedding <=> q.vec) AS rank,
               a.name AS title,
               COALESCE(a.brand, '') || ' ' || COALESCE(a.controller, '') AS source,
               LEFT(COALESCE(a.description, '') || ' ' || COALESCE(a.action, ''), @preview) AS content
          FROM kb.alarms a
          CROSS JOIN q
         WHERE a.embedding IS NOT NULL
         {BRAND_FILTER}
         ORDER BY a.embedding <=> q.vec
         LIMIT @topN
        """;

    public async Task<(List<Hit> Hits, int Ms)> RecallAsync(Vector queryVec, int topN, string? brand, int previewChars, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var brandFilter = string.IsNullOrEmpty(brand) ? "" : "AND d.brand = @brand";
        var brandFilterA = string.IsNullOrEmpty(brand) ? "" : "AND a.brand = @brand";

        var chunksSql = ChunksSql.Replace("{BRAND_FILTER}", brandFilter);
        var alarmsSql = AlarmsSql.Replace("{BRAND_FILTER}", brandFilterA);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var hits = new List<Hit>();

        var chunkRows = await conn.QueryAsync<dynamic>(new CommandDefinition(
            chunksSql,
            new { vec = queryVec.ToString(), preview = previewChars, topN, brand },
            cancellationToken: ct));
        foreach (var r in chunkRows)
        {
            hits.Add(new Hit
            {
                Type = "chunk", Id = (long)r.id, Score = (double)r.score, Rank = (int)r.rank,
                Channel = HitChannel.Vector, Title = r.title?.ToString() ?? "", Source = (r.source?.ToString() ?? "").Trim(),
                Content = r.content?.ToString() ?? "",
            });
        }
        var alarmRows = await conn.QueryAsync<dynamic>(new CommandDefinition(
            alarmsSql,
            new { vec = queryVec.ToString(), preview = previewChars, topN, brand },
            cancellationToken: ct));
        foreach (var r in alarmRows)
        {
            hits.Add(new Hit
            {
                Type = "alarm", Id = (long)r.id, Score = (double)r.score, Rank = (int)r.rank,
                Channel = HitChannel.Vector, Title = r.title?.ToString() ?? "", Source = (r.source?.ToString() ?? "").Trim(),
                Content = r.content?.ToString() ?? "",
            });
        }
        sw.Stop();
        return (hits, (int)sw.ElapsedMilliseconds);
    }
}
