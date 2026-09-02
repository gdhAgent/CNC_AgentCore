// Application/Agent/Tools/QueryAlarmCodeTool.cs —— 工具 2（精确 + trgm）
using System.Data;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace CNC_AgentCore.Application.Agent.Tools;

public sealed class QueryAlarmCodeTool : IToolHandler
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;
    private readonly double _trgmThreshold;
    private readonly int _trgmLimit;
    private readonly bool _enableTrgmFallback;

    public QueryAlarmCodeTool(
        Npgsql.NpgsqlDataSource dataSource,
        double trgmThreshold = 0.3,
        int trgmLimit = 5,
        bool enableTrgmFallback = true)
    {
        _dataSource = dataSource;
        _trgmThreshold = trgmThreshold;
        _trgmLimit = trgmLimit;
        _enableTrgmFallback = enableTrgmFallback;
    }

    public ToolSpec Spec { get; } = new(
        name: "query_alarm_code",
        description: "按报警码精确查询报警知识库，返回报警名称、可能原因、处置步骤与安全提示。"
                   + "码输错 1~2 位时自动给出\"您是否想问\"候选。当用户明确给出报警码时使用。",
        parameters: new Dictionary<string, object>
        {
            ["code"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "报警码，如 SV0401 / AL24 / 3001",
            },
            ["brand"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "品牌（可选）：FANUC / MITSUBISHI / SIEMENS",
            },
        },
        required: new[] { "code" });

    private const string ExactSql = """
        SELECT brand, controller, code_norm, name, category, severity,
               description, cause, action, safety_note
          FROM kb.alarms
         WHERE code_norm = @code
           AND (@brand::text IS NULL OR brand = @brand)
         LIMIT 1
        """;

    private const string SuggestSql = """
        SELECT code_norm, name, brand, controller,
               similarity(code_norm, @code) AS score
          FROM kb.alarms
         WHERE code_norm::text % @code
           AND similarity(code_norm, @code) >= @threshold
           AND (@brand::text IS NULL OR brand = @brand)
         ORDER BY similarity(code_norm, @code) DESC
         LIMIT @limit
        """;

    public async Task<(string Output, Dictionary<string, object?>? Structured)> ExecuteAsync(
        IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var code = args.TryGetValue("code", out var c) ? c?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException("query_alarm_code: 参数 code 不能为空");
        var brand = args.TryGetValue("brand", out var b) ? b?.ToString()?.Trim().ToUpperInvariant() : null;
        if (string.IsNullOrWhiteSpace(brand)) brand = null;

        var structured = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["exact"] = null,
            ["suggests"] = new List<Dictionary<string, object?>>(),
        };

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 1) 精确查
        var exact = await conn.QueryFirstOrDefaultAsync<(string?, string?, string, string, string?, string?, string?, string?, string?, string?)>(
            new CommandDefinition(ExactSql, new { code = code, brand = brand }, cancellationToken: ct));

        if (!string.IsNullOrEmpty(exact.Item3))
        {
            var (brandVal, controller, codeNorm, name, category, severity, desc, cause, action, safetyNote) = exact;
            structured["exact"] = new
            {
                brand = brandVal,
                controller,
                code_norm = codeNorm,
                name,
                category,
                severity,
                description = desc,
                cause,
                action,
                safety_note = safetyNote,
            };
            var output = RenderAlarm(brandVal, controller, codeNorm, name, category, severity, desc, cause, action, safetyNote);
            return (output, structured);
        }

        // 2) 模糊纠错
        if (!_enableTrgmFallback)
            return ($"报警码 {code} 未在知识库中找到，且无相近候选。", structured);

        var suggests = await conn.QueryAsync<(string CodeNorm, string Name, string? Brand, string? Controller, double Score)>(
            new CommandDefinition(SuggestSql,
                new { code = code, brand = brand, threshold = _trgmThreshold, limit = _trgmLimit },
                cancellationToken: ct));

        var list = new List<Dictionary<string, object?>>();
        var codes = new List<string>();
        foreach (var s in suggests)
        {
            list.Add(new Dictionary<string, object?>
            {
                ["code_norm"] = s.CodeNorm,
                ["name"] = s.Name,
                ["brand"] = s.Brand,
                ["controller"] = s.Controller,
                ["score"] = Math.Round(s.Score, 3),
            });
            codes.Add(s.CodeNorm);
        }
        structured["suggests"] = list;

        if (codes.Count > 0)
        {
            var sugCodes = string.Join("、", codes);
            return ($"报警码 {code} 在知识库中未精确命中。\n您是否想问：{sugCodes}？\n（请先向用户确认正确的报警码，再给出处置建议）",
                    structured);
        }
        return ($"报警码 {code} 未在知识库中找到，且无相近候选。", structured);
    }

    private static string RenderAlarm(string? brand, string? controller, string codeNorm, string name,
        string? category, string? severity, string? description, string? cause, string? action, string? safetyNote)
    {
        var lines = new List<string>
        {
            $"报警 {codeNorm} {name}（{brand ?? ""} {controller ?? "通用"}）",
            $"类别：{category ?? "未知"}｜严重度：{severity ?? "unknown"}",
        };
        foreach (var (label, val) in new[] { ("现象", description), ("可能原因", cause), ("处置步骤", action) })
        {
            if (!string.IsNullOrEmpty(val)) lines.Add($"{label}：{val}");
        }
        if (!string.IsNullOrEmpty(safetyNote)) lines.Add($"⚠️ 安全提示：{safetyNote}");
        return string.Join("\n", lines);
    }
}
