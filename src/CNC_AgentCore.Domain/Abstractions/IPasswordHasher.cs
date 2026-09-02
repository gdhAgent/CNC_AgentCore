// Domain/Abstractions/IPasswordHasher.cs —— PBKDF2 哈希接口
namespace CNC_AgentCore.Domain.Abstractions;

public interface IPasswordHasher
{
    string EncodeHash(string password, int? iterations = null);

    bool VerifyPassword(string password, string storedHash);

    bool NeedsRehash(string storedHash, int? currentIterations = null);
}
