// Domain/Abstractions/IUserRepository.cs —— ops.users 仓储
using CNC_AgentCore.Domain.Entities;

namespace CNC_AgentCore.Domain.Abstractions;

public interface IUserRepository
{
    Task<(List<User> Items, long Total)> ListAsync(
        string? role, bool? isActive, string? q, int limit, int offset, CancellationToken ct = default);

    Task<User?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>新增用户；username 冲突抛 <see cref="UserExistsException"/>（API 层映射 409）。</summary>
    Task<long> CreateAsync(
        string username, string displayName, string passwordHash, string role,
        bool isActive, string? createdBy, CancellationToken ct = default);

    /// <summary>仅更新 display_name / role / is_active（密码改密走 UpdatePasswordAsync）。</summary>
    Task<bool> UpdateAsync(long id, string? displayName, string? role, bool? isActive, CancellationToken ct = default);

    Task<bool> UpdatePasswordAsync(long id, string passwordHash, CancellationToken ct = default);

    Task TouchLastLoginAsync(long id, CancellationToken ct = default);

    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

public sealed class UserExistsException : Exception
{
    public UserExistsException(string message) : base(message) { }
}
