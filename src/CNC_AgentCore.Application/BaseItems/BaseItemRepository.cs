// Application/BaseItems/BaseItemRepository.cs —— kb.base_items 枚举字典（Dapper）。
// Dapper 规则：多词蛇形列单 token 别名；可写 row 类 + 投影。
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.BaseItems;

public sealed class BaseItemRepository : IBaseItemRepository
{
    private readonly NpgsqlDataSource _ds;

    public BaseItemRepository(NpgsqlDataSource ds) => _ds = ds;

    // 行模型：可写属性类
    private sealed class BaseItemRow
    {
        public long Id { get; set; }
        public string Kind { get; set; } = "";
        public string Code { get; set; } = "";
        public string LabelZh { get; set; } = "";
        public string LabelEn { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public async Task<(List<BaseItem> Items, int Total)> ListAsync(
        string? kind, bool includeInactive, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, kind, code, label_zh AS LabelZh, label_en AS LabelEn,
                   sort_order AS SortOrder, is_active AS IsActive,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM kb.base_items
             WHERE (@kind::text IS NULL OR kind = @kind)
               AND (@showAll::bool OR is_active = true)
             ORDER BY kind, sort_order, id
            """;
        var p = new DynamicParameters();
        p.Add("kind", kind);
        p.Add("showAll", includeInactive);

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<BaseItemRow>(new CommandDefinition(sql, p, cancellationToken: ct));
        var items = rows.Select(r => new BaseItem(
            r.Id, r.Kind, r.Code, r.LabelZh, r.LabelEn, r.SortOrder, r.IsActive,
            r.CreatedAt, r.UpdatedAt)).ToList();
        return (items, items.Count);
    }

    public async Task<long> CreateAsync(CreateBaseItemRequest req, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO kb.base_items (kind, code, label_zh, label_en, sort_order, is_active)
            VALUES (@kind, @code, @labelZh, @labelEn, @sortOrder, @isActive)
            RETURNING id
            """;
        var p = new DynamicParameters();
        p.Add("kind", req.Kind);
        p.Add("code", req.Code);
        p.Add("labelZh", req.LabelZh);
        p.Add("labelEn", req.LabelEn);
        p.Add("sortOrder", req.SortOrder);
        p.Add("isActive", req.IsActive);

        await using var conn = await _ds.OpenConnectionAsync(ct);
        try
        {
            return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new BaseItemConflictException($"同 kind='{req.Kind}' 下 code='{req.Code}' 已存在");
        }
    }

    public async Task<bool> UpdateAsync(long id, UpdateBaseItemRequest req, CancellationToken ct = default)
    {
        // COALESCE 语义：传 null/缺省保持原值；kind/code 不可改。
        // 可空参数带类型 cast：Npgsql 不猜 null 类型。
        const string sql = """
            UPDATE kb.base_items
               SET label_zh = COALESCE(@labelZh::text, label_zh),
                   label_en = COALESCE(@labelEn::text, label_en),
                   sort_order = COALESCE(@sortOrder::int, sort_order),
                   is_active = COALESCE(@isActive::boolean, is_active),
                   updated_at = now()
             WHERE id = @id
            RETURNING id
            """;
        var p = new DynamicParameters();
        p.Add("labelZh", req.LabelZh);
        p.Add("labelEn", req.LabelEn);
        p.Add("sortOrder", req.SortOrder);
        p.Add("isActive", req.IsActive);
        p.Add("id", id);

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var updated = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, p, cancellationToken: ct));
        return updated.HasValue;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM kb.base_items WHERE id = @id RETURNING id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id }, cancellationToken: ct));
        return deleted.HasValue;
    }
}
