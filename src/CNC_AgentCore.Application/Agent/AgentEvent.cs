// Application/Agent/AgentEvent.cs —— 流式事件。
namespace CNC_AgentCore.Application.Agent;

public sealed class AgentEvent
{
    public string Kind { get; init; } = string.Empty;          // retrieval/tool/delta/done/error
    public Dictionary<string, object?>? Data { get; init; }
    public AgentResult? Result { get; init; }
}
