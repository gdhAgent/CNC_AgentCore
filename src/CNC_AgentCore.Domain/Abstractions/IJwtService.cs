// Domain/Abstractions/IJwtService.cs —— JWT 签发/解码
using CNC_AgentCore.Domain.ValueObjects;

namespace CNC_AgentCore.Domain.Abstractions;

public class JwtError : Exception
{
    public JwtError(string message) : base(message) { }
}

public sealed class JwtExpiredError : JwtError
{
    public JwtExpiredError(string message) : base(message) { }
}

public sealed class JwtInvalidError : JwtError
{
    public JwtInvalidError(string message) : base(message) { }
}

public interface IJwtService
{
    string IssueToken(long uid, string username, string role, string displayName);

    TokenPayload DecodeToken(string token);

    TokenPayload? SafeDecode(string token);

    int RemainingSeconds(TokenPayload payload);
}
