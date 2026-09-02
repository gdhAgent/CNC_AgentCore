// Application/Agent/IAgentRouter.cs —— Agent 状态机入口。
namespace CNC_AgentCore.Application.Agent;

public interface IAgentRouter
{
    Task<AgentResult> RunAsync(string query, CancellationToken ct = default);

    IAsyncEnumerable<AgentEvent> RunStreamAsync(string query, CancellationToken ct = default);
}
