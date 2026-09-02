// Domain/ValueObjects/TokenPayload.cs —— JWT 解码结果
namespace CNC_AgentCore.Domain.ValueObjects;

public sealed class TokenPayload
{
    public long Uid { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long Iat { get; set; }
    public long Exp { get; set; }
}
