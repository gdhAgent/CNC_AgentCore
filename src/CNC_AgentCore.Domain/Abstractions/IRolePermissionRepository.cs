// Domain/Abstractions/IRolePermissionRepository.cs —— 权限矩阵仓储
using CNC_AgentCore.Domain.Entities;

namespace CNC_AgentCore.Domain.Abstractions;

public interface IRolePermissionRepository
{
    Task<IReadOnlyDictionary<string, HashSet<string>>> GetRolePermissionsMapAsync(string role, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRoleVisiblePagesAsync(string role, CancellationToken ct = default);

    Task<IReadOnlyList<RolePermission>> GetAllForRoleAsync(string role, CancellationToken ct = default);

    /// <summary>取全角色全页矩阵（管理 UI 全景视图）。</summary>
    Task<IReadOnlyList<RolePermission>> GetAllAsync(CancellationToken ct = default);

    Task BulkSetAsync(string role, IEnumerable<RolePermission> rows, string updatedBy, CancellationToken ct = default);
}
