// Application/Agent/AgentConfig.cs —— 状态机参数
namespace CNC_AgentCore.Application.Agent;

public sealed class AgentConfig
{
    public int MaxRounds { get; init; } = 2;
    public double ToolTimeoutSec { get; init; } = 8.0;
    public double TotalTimeoutSec { get; init; } = 30.0;
    public double Temperature { get; init; } = 0.3;
    public int MaxTokens { get; init; } = 2048;
}
