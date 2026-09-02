// Application/Retrieval/FulltextSearch.cs —— 全文检索（tsquery 构造 + tsvector 召回）。
using System.Diagnostics;
using System.Text.RegularExpressions;
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;
using Dapper;

namespace CNC_AgentCore.Application.Retrieval;

public sealed class FulltextSearch
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;
    private readonly ITokenizer _tokenizer;

    // tsquery 操作符需要剔除的字符
    private static readonly Regex SpecialTsQueryChars = new(@"[&|!()<\\:'*]", RegexOptions.Compiled);
    private static readonly Regex ValidToken = new(@"^[\w一-鿿-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string ChunksSql = """
        WITH q AS (SELECT to_tsquery('simple', @tsq) AS tsq)
        SELECT 'chunk'::text AS type,
               c.id AS id,
               ts_rank_cd(c.tsv, q.tsq) AS score,
               ROW_NUMBER() OVER (ORDER BY ts_rank_cd(c.tsv, q.tsq) DESC) AS rank,
               c.heading_path AS title,
               COALESCE(d.title, '') || COALESCE(' P' || c.page_from, '') AS source,
               LEFT(c.content, @preview) AS content
          FROM kb.chunks c
          CROSS JOIN q
          LEFT JOIN kb.documents d ON d.id = c.doc_id
         WHERE c.level = 2 AND c.tsv @@ q.tsq
         {BRAND_FILTER}
         ORDER BY ts_rank_cd(c.tsv, q.tsq) DESC
         LIMIT @topN
        """;

    private const string AlarmsSql = """
        WITH q AS (SELECT to_tsquery('simple', @tsq) AS tsq)
        SELECT 'alarm'::text AS type,
               a.id AS id,
               ts_rank_cd(a.tsv, q.tsq) AS score,
               ROW_NUMBER() OVER (ORDER BY ts_rank_cd(a.tsv, q.tsq) DESC) AS rank,
               a.name AS title,
               COALESCE(a.brand, '') || ' ' || COALESCE(a.controller, '') AS source,
               LEFT(COALESCE(a.description, '') || ' ' || COALESCE(a.action, ''), @preview) AS content
          FROM kb.alarms a
          CROSS JOIN q
         WHERE a.tsv @@ q.tsq
         {BRAND_FILTER}
         ORDER BY ts_rank_cd(a.tsv, q.tsq) DESC
         LIMIT @topN
        """;

    public FulltextSearch(Npgsql.NpgsqlDataSource dataSource, ITokenizer tokenizer)
    {
        _dataSource = dataSource;
        _tokenizer = tokenizer;
    }

    public string BuildTsQuery(string text, Dictionary<string, string[]>? synonymMap = null, bool enableSynonyms = true)
    {
        var tokens = _tokenizer.Tokenize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expanded = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tok in tokens)
        {
            var safe = EscapeToken(tok);
            if (safe is null || !seen.Add(safe)) continue;
            expanded.Add(safe);

            if (enableSynonyms && synonymMap is not null)
            {
                foreach (var syn in SynonymsForToken(safe, synonymMap))
                {
                    var safeSyn = EscapeToken(syn);
                    if (safeSyn is null || !seen.Add(safeSyn)) continue;
                    expanded.Add(safeSyn);
                }
            }
        }
        return expanded.Count == 0 ? "" : string.Join(" | ", expanded.Select(t => $"'{t}'"));
    }

    private static IEnumerable<string> SynonymsForToken(string token, Dictionary<string, string[]> map)
    {
        if (map.TryGetValue(token, out var syns)) return syns.Where(s => s != token);
        if (map.TryGetValue(token.ToLowerInvariant(), out var syns2)) return syns2.Where(s => s != token);
        return Array.Empty<string>();
    }

    private static string? EscapeToken(string? tok)
    {
        if (string.IsNullOrEmpty(tok)) return null;
        tok = tok.Trim();
        if (string.IsNullOrEmpty(tok)) return null;
        if (SpecialTsQueryChars.IsMatch(tok)) return null;
        if (!ValidToken.IsMatch(tok)) return null;
        if (tok.All(char.IsDigit) && tok.Length < 3) return null;
        if (tok.Length < 2 && !tok.All(char.IsLetterOrDigit)) return null;
        return tok;
    }

    public async Task<Dictionary<string, string[]>?> LoadSynonymMapAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(string Canonical, string[] Synonyms)>(
            new CommandDefinition("SELECT canonical, synonyms FROM kb.term_dict WHERE array_length(synonyms, 1) > 0",
                cancellationToken: ct));
        var map = new Dictionary<string, string[]>();
        foreach (var (canon, syns) in rows)
        {
            var c = (canon ?? "").Trim();
            if (string.IsNullOrEmpty(c)) continue;
            if (!map.ContainsKey(c)) map[c] = syns ?? Array.Empty<string>();
            foreach (var s in syns ?? Array.Empty<string>())
            {
                if (!map.ContainsKey(s)) map[s] = new[] { c };
            }
        }
        return map;
    }

    public async Task<(List<Hit> Hits, int Ms)> RecallAsync(string queryText, int topN, string? brand, int previewChars,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tokens = _tokenizer.Tokenize(queryText);
        if (string.IsNullOrEmpty(tokens)) return (new List<Hit>(), 0);

        var synMap = await LoadSynonymMapAsync(ct);
        var tsq = BuildTsQuery(queryText, synMap, enableSynonyms: true);
        if (string.IsNullOrEmpty(tsq)) return (new List<Hit>(), 0);

        var brandFilter = string.IsNullOrEmpty(brand) ? "" : "AND d.brand = @brand";
        var brandFilterA = string.IsNullOrEmpty(brand) ? "" : "AND a.brand = @brand";
        var chunksSql = ChunksSql.Replace("{BRAND_FILTER}", brandFilter);
        var alarmsSql = AlarmsSql.Replace("{BRAND_FILTER}", brandFilterA);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var hits = new List<Hit>();

        var chunkRows = await conn.QueryAsync<dynamic>(new CommandDefinition(chunksSql,
            new { tsq, preview = previewChars, topN, brand }, cancellationToken: ct));
        foreach (var r in chunkRows)
        {
            hits.Add(new Hit
            {
                Type = "chunk", Id = (long)r.id, Score = (double)r.score, Rank = (int)r.rank,
                Channel = HitChannel.Fulltext, Title = r.title?.ToString() ?? "", Source = (r.source?.ToString() ?? "").Trim(),
                Content = r.content?.ToString() ?? "",
            });
        }
        var alarmRows = await conn.QueryAsync<dynamic>(new CommandDefinition(alarmsSql,
            new { tsq, preview = previewChars, topN, brand }, cancellationToken: ct));
        foreach (var r in alarmRows)
        {
            hits.Add(new Hit
            {
                Type = "alarm", Id = (long)r.id, Score = (double)r.score, Rank = (int)r.rank,
                Channel = HitChannel.Fulltext, Title = r.title?.ToString() ?? "", Source = (r.source?.ToString() ?? "").Trim(),
                Content = r.content?.ToString() ?? "",
            });
        }
        sw.Stop();
        return (hits, (int)sw.ElapsedMilliseconds);
    }
}
