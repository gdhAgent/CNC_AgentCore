// Application/Agent/Tools/ToolSpec.cs —— 工具 schema 与执行结果。
namespace CNC_AgentCore.Application.Agent.Tools;

public sealed class ToolSpec
{
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
    public IReadOnlyList<string> Required { get; }

    public ToolSpec(string name, string description, IReadOnlyDictionary<string, object> parameters, IReadOnlyList<string>? required = null)
    {
        Name = name;
        Description = description;
        Parameters = parameters;
        Required = required ?? Array.Empty<string>();
    }

    public Dictionary<string, object?> ToOpenAISchema() => new()
    {
        ["type"] = "function",
        ["function"] = new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["description"] = Description,
            ["parameters"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = Parameters,
                ["required"] = Required,
            },
        },
    };
}

public sealed class ToolResult
{
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, object?> Args { get; init; } = new();
    public string Output { get; init; } = string.Empty;        // 给 LLM 的观察文本
    public bool Ok { get; init; } = true;
    public int Ms { get; init; }
    public bool TimedOut { get; init; }
    public Dictionary<string, object?>? Structured { get; init; }
}
