// Application/Auth/CurrentUser.cs —— 当前登录用户上下文。
namespace CNC_AgentCore.Application.Auth;

public sealed record CurrentUser(long Uid, string Username, string Role, string DisplayName);
