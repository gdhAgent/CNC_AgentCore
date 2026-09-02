// Application/Agent/Tools/RetrieveKnowledgeTool.cs —— 工具 1
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.ValueObjects;

namespace CNC_AgentCore.Application.Agent.Tools;

public sealed class RetrieveKnowledgeTool : IToolHandler
{
    private readonly IRetrievalService _retrieval;
    private readonly RetrievalServiceConfig _config;

    public RetrieveKnowledgeTool(IRetrievalService retrieval, RetrievalServiceConfig config)
    {
        _retrieval = retrieval;
        _config = config;
    }

    public ToolSpec Spec { get; } = new(
        name: "retrieve_knowledge",
        description: "检索知识库（报警码表 + 设备手册 + FAQ + 相似故障工单），返回带来源的 TopN 原文。"
                   + "当用户描述故障现象、询问报警码含义与处置、查询保养或操作步骤时使用。"
                   + "返回的 [n] 编号可直接引用为答案依据。",
        parameters: new Dictionary<string, object>
        {
            ["query"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "故障现象白话描述或报警码，如 '主轴异响'、'SV0401'、'3号机报3001'",
            },
            ["machine_model"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "机台机型（可选），如 VMC850 / TC500；当前为预留参数",
            },
            ["doc_type"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "限定文档类型（可选）：manual / alarm_table / sop / faq；当前为预留参数",
            },
        },
        required: new[] { "query" });

    public async Task<(string Output, Dictionary<string, object?>? Structured)> ExecuteAsync(
        IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var query = args.TryGetValue("query", out var q) ? q?.ToString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("retrieve_knowledge: 参数 query 不能为空");

        var result = await _retrieval.RunQueryAsync(query, _config, ct);
        return RenderResult(result);
    }

    internal static (string Output, Dictionary<string, object?> Structured) RenderResult(QueryResult result)
    {
        var structured = new Dictionary<string, object?>
        {
            ["route"] = result.Route,
            ["refused"] = result.Refused,
            ["refused_reason"] = result.RefusedReason,
            ["detected_codes"] = result.DetectedCodes,
            ["timing"] = result.Timing.AsDict(),
            ["trace_steps"] = result.TraceSteps,
            ["topk"] = result.Topk.Select((h, i) =>
            {
                // code_norm 只在 alarm 类型上携带
                var codeNorm = h.Type == "alarm" && h.Extra.TryGetValue("code_norm", out var cn) && cn is not null
                    ? cn.ToString()
                    : null;
                // highlight：默认 [] 避免下游 missing key
                List<string> highlight;
                if (h.Extra.TryGetValue("highlight", out var hl) && hl is IEnumerable<object?> list)
                    highlight = list.Select(x => x?.ToString() ?? "").ToList();
                else
                    highlight = new List<string>();
                return new Dictionary<string, object?>
                {
                    ["ref"] = i + 1,
                    ["type"] = h.Type,
                    ["id"] = h.Id,
                    ["score"] = Math.Round(h.Score, 4),
                    ["channel"] = h.Channel,
                    ["title"] = h.Title,
                    ["source"] = h.Source,
                    ["content"] = h.Content,
                    ["code_norm"] = codeNorm,
                    ["highlight"] = highlight,
                };
            }).ToList(),
        };

        if (result.Refused)
        {
            return ($"知识库检索未命中（无相关内容或置信度过低），无法给出可靠答案。拒绝原因：{result.RefusedReason ?? "未知"}",
                    structured);
        }

        var lines = new List<string>();
        for (var i = 0; i < result.Topk.Count; i++)
        {
            var h = result.Topk[i];
            var src = string.IsNullOrEmpty(h.Source) ? "未知来源" : h.Source;
            // alarm 命中时把码拼进标题，LLM 引用可直接见码（如 "[1] SV0401 伺服 V-Ready..."）
            var codeNormStr = h.Extra.TryGetValue("code_norm", out var cn2) && cn2 is not null ? cn2.ToString() : null;
            var titleForObs = !string.IsNullOrEmpty(codeNormStr) ? $"{codeNormStr} {h.Title}" : h.Title;
            lines.Add($"[{i + 1}] (来源: {src}) {titleForObs}\n{h.Content}");
        }
        if (result.SuggestHits.Count > 0)
        {
            var sug = string.Join("、",
                result.SuggestHits.Select(h => h.Extra.TryGetValue("code_norm", out var c) && c is not null
                    ? c.ToString()
                    : h.Title).Distinct());
            lines.Add($"（提示：您是否想问 {sug}？）");
        }
        var output = lines.Count > 0 ? string.Join("\n\n", lines) : "知识库未命中。";
        return (output, structured);
    }
}

public interface IToolHandler
{
    ToolSpec Spec { get; }
    Task<(string Output, Dictionary<string, object?>? Structured)> ExecuteAsync(
        IReadOnlyDictionary<string, object?> args, CancellationToken ct);
}
