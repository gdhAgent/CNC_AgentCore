// Application/Agent/AgentResult.cs —— Agent 最终结果。
using CNC_AgentCore.Domain.ValueObjects;

namespace CNC_AgentCore.Application.Agent;

public sealed class AgentResult
{
    public string Answer { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;           // agent/rag_fallback/refused
    public int Rounds { get; init; }
    public bool Degraded { get; init; }
    public List<Dictionary<string, object?>> ToolCalls { get; init; } = new();
    public int TotalMs { get; init; }
    public bool Refused { get; init; }
    public string? RefusedReason { get; init; }
    public string? Error { get; init; }
    public StructuredAnalysis? Analysis { get; init; }
    public string? RawAnswer { get; init; }
    public Guid TraceId { get; init; } = Guid.NewGuid();
    public List<Dictionary<string, object?>> TraceSteps { get; set; } = new();
}

public sealed class StructuredAnalysis
{
    public string Summary { get; set; } = string.Empty;
    public List<PossibleCause> PossibleCauses { get; set; } = new();
    public List<TroubleshootingStep> TroubleshootingSteps { get; set; } = new();
    public List<string> RequiredTools { get; set; } = new();
    public string SafetyNote { get; set; } = string.Empty;
    public bool NeedExpert { get; set; }

    public bool HasContent => !string.IsNullOrEmpty(Summary) || PossibleCauses.Count > 0 || TroubleshootingSteps.Count > 0;

    public Dictionary<string, object?> ToDict() => new()
    {
        ["summary"] = Summary,
        ["possible_causes"] = PossibleCauses.Select(c => new Dictionary<string, object?>
        {
            ["cause"] = c.Cause,
            ["confidence"] = c.Confidence,
            ["refs"] = c.Refs,
        }).ToList(),
        ["troubleshooting_steps"] = TroubleshootingSteps.Select(s => new Dictionary<string, object?>
        {
            ["step"] = s.Step,
            ["action"] = s.Action,
            ["refs"] = s.Refs,
        }).ToList(),
        ["required_tools"] = RequiredTools,
        ["safety_note"] = SafetyNote,
        ["need_expert"] = NeedExpert,
    };
}

public sealed class PossibleCause
{
    public string Cause { get; set; } = string.Empty;
    public string Confidence { get; set; } = "medium";
    public List<int> Refs { get; set; } = new();
}

public sealed class TroubleshootingStep
{
    public int Step { get; set; }
    public string Action { get; set; } = string.Empty;
    public List<int> Refs { get; set; } = new();
}
