// Application/Retrieval/CodeExtractor.cs —— 报警码正则抽取 + 精确短路 + trgm 模糊
using System.Text.RegularExpressions;
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;
using Dapper;

namespace CNC_AgentCore.Application.Retrieval;

public sealed partial record CodeExtractResult(
    IReadOnlyList<string> DetectedCodes,
    IReadOnlyList<Hit> ExactHits,
    IReadOnlyList<Hit> SuggestHits);

public sealed partial class CodeExtractor
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;
    private readonly double _trgmThreshold;
    private readonly int _trgmLimit;
    private readonly bool _enableTrgm;

    // 1) 带字母前缀（FANUC SV/SP/PS/OT/PW/SR/DS/IO/EX + 三菱 AL/CM）
    [GeneratedRegex(@"\b(?<code>(?:SV|SP|PS|OT|PW|SR|DS|IO|EX|AL|CM)\d{2,6})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixCodeRegex();

    // 2) 纯数字 ≥4 位
    [GeneratedRegex(@"\b(?<code>\d{4,6})\b")]
    private static partial Regex PureDigitRegex();

    // 3) 特殊码 EMG
    [GeneratedRegex(@"\b(?<code>EMG)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpecialRegex();

    public CodeExtractor(Npgsql.NpgsqlDataSource dataSource, double trgmThreshold = 0.3, int trgmLimit = 5, bool enableTrgm = true)
    {
        _dataSource = dataSource;
        _trgmThreshold = trgmThreshold;
        _trgmLimit = trgmLimit;
        _enableTrgm = enableTrgm;
    }

    public string[] ExtractCodes(string query)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var pat in new[] { PrefixCodeRegex(), PureDigitRegex(), SpecialRegex() })
        {
            foreach (Match m in pat.Matches(query))
            {
                var code = m.Groups["code"].Value.ToUpperInvariant();
                if (seen.Add(code)) ordered.Add(code);
            }
        }
        return ordered.ToArray();
    }

    private const string ExactSql = """
        SELECT 'alarm'::text AS type,
               a.id AS id,
               1.0 AS score,
               a.code_norm AS code_norm,
               a.name AS title,
               COALESCE(a.brand, '') || ' ' || COALESCE(a.controller, '') AS source,
               LEFT(COALESCE(a.description, '') || ' ' || COALESCE(a.action, ''), @preview) AS content
          FROM kb.alarms a
         WHERE a.code_norm = ANY(@codes)
         ORDER BY array_position(@codes, a.code_norm)
        """;

    private const string SuggestSql = """
        SELECT 'alarm'::text AS type,
               a.id AS id,
               similarity(a.code_norm, @code) AS score,
               a.code_norm AS code_norm,
               a.name AS title,
               COALESCE(a.brand, '') || ' ' || COALESCE(a.controller, '') AS source,
               LEFT(COALESCE(a.description, '') || ' ' || COALESCE(a.action, ''), @preview) AS content
          FROM kb.alarms a
         WHERE a.code_norm::text % @code
           AND similarity(a.code_norm, @code) >= @threshold
         ORDER BY similarity(a.code_norm, @code) DESC
         LIMIT @limit
        """;

    public async Task<CodeExtractResult> ExtractAndMatchAsync(string query, int previewChars = 240, CancellationToken ct = default)
    {
        var detected = ExtractCodes(query);
        if (detected.Length == 0)
            return new CodeExtractResult(Array.Empty<string>(), Array.Empty<Hit>(), Array.Empty<Hit>());

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 1) 精确查
        var exactRows = await conn.QueryAsync<dynamic>(
            new CommandDefinition(ExactSql, new { codes = detected, preview = previewChars }, cancellationToken: ct));
        var exactHits = new List<Hit>();
        foreach (var r in exactRows)
        {
            exactHits.Add(new Hit
            {
                Type = "alarm",
                Id = (long)r.id,
                Score = 1.0,
                Channel = HitChannel.Exact,
                Title = r.title?.ToString() ?? string.Empty,
                Source = (r.source?.ToString() ?? string.Empty).Trim(),
                Content = r.content?.ToString() ?? string.Empty,
                Extra = new() { ["code_norm"] = r.code_norm?.ToString() },
            });
        }

        // 2) 模糊纠错
        var suggestHits = new List<Hit>();
        if (_enableTrgm)
        {
            var seen = new HashSet<long>();
            foreach (var code in detected)
            {
                var rows = await conn.QueryAsync<dynamic>(
                    new CommandDefinition(SuggestSql,
                        new { code, preview = previewChars, threshold = _trgmThreshold, limit = _trgmLimit },
                        cancellationToken: ct));
                foreach (var r in rows)
                {
                    var id = (long)r.id;
                    if (!seen.Add(id)) continue;
                    suggestHits.Add(new Hit
                    {
                        Type = "alarm",
                        Id = id,
                        Score = (double)r.score,
                        Channel = HitChannel.Suggest,
                        Title = r.title?.ToString() ?? string.Empty,
                        Source = (r.source?.ToString() ?? string.Empty).Trim(),
                        Content = r.content?.ToString() ?? string.Empty,
                        Extra = new() { ["code_norm"] = r.code_norm?.ToString() },
                    });
                }
            }
        }

        return new CodeExtractResult(detected, exactHits, suggestHits);
    }
}
