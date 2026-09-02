// Application/Auth/RolePermissionRepository.cs —— 权限矩阵仓储（Dapper 实现）
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Entities;

namespace CNC_AgentCore.Application.Auth;

public sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;

    public RolePermissionRepository(Npgsql.NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyDictionary<string, HashSet<string>>> GetRolePermissionsMapAsync(string role, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(string PageCode, string[] Actions)>(new CommandDefinition(
            "SELECT page_code, actions FROM ops.role_permissions WHERE role = @role AND can_access = true",
            new { role }, cancellationToken: ct));
        var map = new Dictionary<string, HashSet<string>>();
        foreach (var (page, actions) in rows)
        {
            if (!map.TryGetValue(page, out var set))
                map[page] = set = new HashSet<string>();
            foreach (var a in actions ?? Array.Empty<string>()) set.Add(a);
        }
        return map;
    }

    public async Task<IReadOnlyList<string>> GetRoleVisiblePagesAsync(string role, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT page_code FROM ops.role_permissions WHERE role = @role AND can_access = true ORDER BY page_code",
            new { role }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RolePermission>> GetAllForRoleAsync(string role, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RolePermission>(new CommandDefinition(
            @"SELECT id, role, page_code AS PageCode, can_access AS CanAccess, actions, updated_at AS UpdatedAt, updated_by AS UpdatedBy
              FROM ops.role_permissions WHERE role = @role ORDER BY page_code",
            new { role }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>取全角色全页权限矩阵，供管理 UI 全景视图。</summary>
    public async Task<IReadOnlyList<RolePermission>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RolePermission>(new CommandDefinition(
            @"SELECT id, role, page_code AS PageCode, can_access AS CanAccess, actions, updated_at AS UpdatedAt, updated_by AS UpdatedBy
              FROM ops.role_permissions ORDER BY role, page_code",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task BulkSetAsync(string role, IEnumerable<RolePermission> rows, string updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM ops.role_permissions WHERE role = @role",
                new { role }, transaction: tx, cancellationToken: ct));
            foreach (var r in rows)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO ops.role_permissions (role, page_code, can_access, actions, updated_at, updated_by)
                      VALUES (@role, @pageCode, @canAccess, @actions, now(), @updatedBy)",
                    new
                    {
                        role,
                        pageCode = r.PageCode,
                        canAccess = r.CanAccess,
                        actions = r.Actions,
                        updatedBy,
                    }, transaction: tx, cancellationToken: ct));
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
