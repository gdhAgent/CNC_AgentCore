// Application/Auth/UserRepository.cs —— ops.users 仓储（Dapper）。
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Entities;
using CNC_AgentCore.Domain.Enums;
using Npgsql;

namespace CNC_AgentCore.Application.Auth;

public sealed class UserRepository : IUserRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    // 行模型：Role 存库为字符串，显式映射实体枚举。用可写类非 record —— Dapper 按列名匹配 ctor 参数，record 位置参数易失配。
    private sealed class UserRow
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    private const string Cols =
        "id AS Id, username AS Username, display_name AS DisplayName, password_hash AS PasswordHash, " +
        "role AS Role, is_active AS IsActive, last_login_at AS LastLoginAt, created_at AS CreatedAt, " +
        "updated_at AS UpdatedAt, created_by AS CreatedBy";

    public async Task<(List<User> Items, long Total)> ListAsync(
        string? role, bool? isActive, string? q, int limit, int offset, CancellationToken ct = default)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(role)) { where.Add("role = @role"); p.Add("role", role); }
        if (isActive is not null) { where.Add("is_active = @isActive"); p.Add("isActive", isActive.Value); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            where.Add("(username ILIKE @q OR display_name ILIKE @q)");
            p.Add("q", $"%{q}%");
        }
        var cond = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition($"SELECT count(*) FROM ops.users{cond}", p, cancellationToken: ct));

        p.Add("limit", limit);
        p.Add("offset", offset);
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(
            $"SELECT {Cols} FROM ops.users{cond} ORDER BY id LIMIT @limit OFFSET @offset", p, cancellationToken: ct));
        return (rows.Select(ToEntity).ToList(), total);
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(
            $"SELECT {Cols} FROM ops.users WHERE id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : ToEntity(row);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<UserRow>(new CommandDefinition(
            $"SELECT {Cols} FROM ops.users WHERE username = @username", new { username }, cancellationToken: ct));
        return row is null ? null : ToEntity(row);
    }

    public async Task<long> CreateAsync(
        string username, string displayName, string passwordHash, string role,
        bool isActive, string? createdBy, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ops.users (username, display_name, password_hash, role, is_active, created_by)
            VALUES (@username, @displayName, @passwordHash, @role, @isActive, @createdBy)
            RETURNING id
            """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        try
        {
            return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql,
                new { username, displayName, passwordHash, role, isActive, createdBy }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new UserExistsException($"username '{username}' already exists");
        }
    }

    public async Task<bool> UpdateAsync(long id, string? displayName, string? role, bool? isActive, CancellationToken ct = default)
    {
        var sets = new List<string>();
        var p = new DynamicParameters();
        if (displayName is not null) { sets.Add("display_name = @displayName"); p.Add("displayName", displayName); }
        if (role is not null) { sets.Add("role = @role"); p.Add("role", role); }
        if (isActive is not null) { sets.Add("is_active = @isActive"); p.Add("isActive", isActive.Value); }
        if (sets.Count == 0) throw new ArgumentException("display_name/role/is_active 至少一个");
        sets.Add("updated_at = now()");
        p.Add("id", id);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE ops.users SET {string.Join(", ", sets)} WHERE id = @id", p, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> UpdatePasswordAsync(long id, string passwordHash, CancellationToken ct = default)
    {
        const string sql = "UPDATE ops.users SET password_hash = @passwordHash, updated_at = now() WHERE id = @id";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { id, passwordHash }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task TouchLastLoginAsync(long id, CancellationToken ct = default)
    {
        const string sql = "UPDATE ops.users SET last_login_at = now() WHERE id = @id";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM ops.users WHERE id = @id";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return affected > 0;
    }

    private static User ToEntity(UserRow r) => new()
    {
        Id = r.Id,
        Username = r.Username,
        DisplayName = r.DisplayName,
        PasswordHash = r.PasswordHash,
        Role = ParseRole(r.Role),
        IsActive = r.IsActive,
        LastLoginAt = r.LastLoginAt,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        CreatedBy = r.CreatedBy,
    };

    internal static Role ParseRole(string r) => r switch
    {
        "admin" => Role.Admin,
        "operator" => Role.Operator,
        _ => Role.Viewer,
    };
}
