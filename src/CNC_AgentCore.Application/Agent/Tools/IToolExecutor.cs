// Application/Agent/Tools/IToolExecutor.cs —— 工具执行接口（3 个受限工具）。
namespace CNC_AgentCore.Application.Agent.Tools;

public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(string toolName, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);

    IReadOnlyList<ToolSpec> GetAllToolSpecs();
}
