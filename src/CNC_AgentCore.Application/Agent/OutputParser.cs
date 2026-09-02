// Application/Agent/OutputParser.cs —— LLM 输出解析 + 引用校验 + 拒答判定。
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CNC_AgentCore.Application.Agent;

public static class OutputParser
{
    // 三级宽松 JSON 提取（纯 JSON / ```json``` 围栏 / 嵌入文本）
    public static Dictionary<string, object?>? ExtractJsonObject(string? text)
    {
        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) return null;

        // 1) 直接解析
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, object?>>(text);
            if (d is not null) return d;
        }
        catch { /* try next */ }

        // 2) ```json ... ``` 围栏
        var m = Regex.Match(text, @"```(?:json)?\s*(.*?)```", RegexOptions.Singleline);
        if (m.Success)
        {
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, object?>>(m.Groups[1].Value);
                if (d is not null) return d;
            }
            catch { /* try next */ }
        }

        // 3) 第一个 { 到最后一个 }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, object?>>(text.Substring(start, end - start + 1));
                if (d is not null) return d;
            }
            catch { /* give up */ }
        }
        return null;
    }

    private static List<int> CleanRefs(object? raw)
    {
        var result = new List<int>();
        if (raw is not JsonElement je) return result;
        if (je.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in je.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n) && n >= 1 && !result.Contains(n))
                result.Add(n);
        }
        result.Sort();
        return result;
    }

    public static StructuredAnalysis ParseAnalysis(string? text)
    {
        var data = ExtractJsonObject(text);
        if (data is null) return new StructuredAnalysis();

        var causes = new List<PossibleCause>();
        if (data.TryGetValue("possible_causes", out var rawCauses) && rawCauses is JsonElement jeCauses && jeCauses.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in jeCauses.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.Object &&
                    c.TryGetProperty("cause", out var causeProp) && causeProp.ValueKind == JsonValueKind.String)
                {
                    var confidence = "medium";
                    if (c.TryGetProperty("confidence", out var confProp) && confProp.ValueKind == JsonValueKind.String)
                        confidence = confProp.GetString() ?? "medium";
                    causes.Add(new PossibleCause
                    {
                        Cause = causeProp.GetString()?.Trim() ?? string.Empty,
                        Confidence = confidence,
                        Refs = CleanRefs(c.TryGetProperty("refs", out var r) ? r : null),
                    });
                }
            }
        }

        var steps = new List<TroubleshootingStep>();
        if (data.TryGetValue("troubleshooting_steps", out var rawSteps) && rawSteps is JsonElement jeSteps && jeSteps.ValueKind == JsonValueKind.Array)
        {
            var idx = 1;
            foreach (var s in jeSteps.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.Object &&
                    s.TryGetProperty("action", out var actProp) && actProp.ValueKind == JsonValueKind.String)
                {
                    var stepNo = idx;
                    if (s.TryGetProperty("step", out var stepProp) && stepProp.ValueKind == JsonValueKind.Number)
                        stepNo = stepProp.GetInt32();
                    steps.Add(new TroubleshootingStep
                    {
                        Step = stepNo,
                        Action = actProp.GetString()?.Trim() ?? string.Empty,
                        Refs = CleanRefs(s.TryGetProperty("refs", out var r) ? r : null),
                    });
                }
                idx++;
            }
        }

        var tools = new List<string>();
        if (data.TryGetValue("required_tools", out var rawTools) && rawTools is JsonElement jeTools && jeTools.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in jeTools.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) tools.Add(s);
                }
            }
        }

        return new StructuredAnalysis
        {
            Summary = data.TryGetValue("summary", out var rawSummary) && rawSummary is JsonElement sum && sum.ValueKind == JsonValueKind.String ? sum.GetString()?.Trim() ?? "" : "",
            PossibleCauses = causes,
            TroubleshootingSteps = steps,
            RequiredTools = tools,
            SafetyNote = data.TryGetValue("safety_note", out var sn) && sn is JsonElement snEl && snEl.ValueKind == JsonValueKind.String ? snEl.GetString()?.Trim() ?? "" : "",
            NeedExpert = data.TryGetValue("need_expert", out var ne) && ne is JsonElement neEl && (neEl.ValueKind == JsonValueKind.True || neEl.ValueKind == JsonValueKind.False) && neEl.GetBoolean(),
        };
    }

    // 引用越界校验：返回新对象
    public static StructuredAnalysis ValidateCitations(StructuredAnalysis analysis, int maxRef)
    {
        if (maxRef < 1)
        {
            return new StructuredAnalysis
            {
                Summary = analysis.Summary,
                RequiredTools = analysis.RequiredTools,
                SafetyNote = analysis.SafetyNote,
                NeedExpert = analysis.NeedExpert,
            };
        }
        List<int> Ok(List<int> refs) => refs.Where(r => 1 <= r && r <= maxRef).ToList();
        return new StructuredAnalysis
        {
            Summary = analysis.Summary,
            PossibleCauses = analysis.PossibleCauses.Select(c =>
                new PossibleCause { Cause = c.Cause, Confidence = c.Confidence, Refs = Ok(c.Refs) }).ToList(),
            TroubleshootingSteps = analysis.TroubleshootingSteps.Select(s =>
                new TroubleshootingStep { Step = s.Step, Action = s.Action, Refs = Ok(s.Refs) }).ToList(),
            RequiredTools = analysis.RequiredTools,
            SafetyNote = analysis.SafetyNote,
            NeedExpert = analysis.NeedExpert,
        };
    }

    // 拒答判定（仅 grounded=False 时触发）
    public static (bool Refused, string? Reason) DecideRefusal(bool grounded, StructuredAnalysis analysis, string? rawText)
    {
        if (grounded) return (false, null);
        if (analysis.PossibleCauses.Count > 0 || analysis.TroubleshootingSteps.Count > 0)
            return (true, "no_grounding");
        if (analysis.NeedExpert) return (true, "insufficient_material");
        if (!analysis.HasContent)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return (true, "no_content");
            if (ExtractJsonObject(rawText) is not null) return (true, "no_content");
        }
        return (false, null);
    }

    // 渲染成可读文本
    public static string RenderAnalysis(StructuredAnalysis analysis)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(analysis.Summary)) parts.Add(analysis.Summary);
        if (analysis.PossibleCauses.Count > 0)
        {
            parts.Add("🔍 可能原因：");
            for (var i = 0; i < analysis.PossibleCauses.Count; i++)
            {
                var c = analysis.PossibleCauses[i];
                var refs = string.Concat(c.Refs.Select(r => $"[{r}]"));
                parts.Add($"{i + 1}. {c.Cause}（{c.Confidence}）{refs}".TrimEnd());
            }
        }
        if (analysis.TroubleshootingSteps.Count > 0)
        {
            parts.Add("🛠 排查步骤：");
            foreach (var s in analysis.TroubleshootingSteps)
            {
                var refs = string.Concat(s.Refs.Select(r => $"[{r}]"));
                parts.Add($"{s.Step}. {s.Action} {refs}".TrimEnd());
            }
        }
        if (analysis.RequiredTools.Count > 0)
            parts.Add($"🔧 所需工具：{string.Join("、", analysis.RequiredTools)}");
        if (!string.IsNullOrEmpty(analysis.SafetyNote))
            parts.Add($"⚠️ 安全提示：{analysis.SafetyNote}");
        if (analysis.NeedExpert)
            parts.Add("⚠️ 建议联系设备工程师进一步确认。");
        return parts.Count > 0 ? string.Join("\n", parts) : "未找到可靠依据。";
    }
}
