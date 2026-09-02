// Application/Devices/DeviceRepository.cs —— ops.machines 设备台账（Dapper）。
// Dapper 规则：多词蛇形列单 token 别名；可写 row 类 + 投影。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Devices;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly NpgsqlDataSource _ds;

    public DeviceRepository(NpgsqlDataSource ds) => _ds = ds;

    // 行模型：可写属性类（Dapper 按列名匹配，不用 record 位置参数）
    private sealed class DeviceRow
    {
        public long Id { get; set; }
        public string AssetNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public string? Model { get; set; }
        public string? Controller { get; set; }
        public string? Workshop { get; set; }
        public string? LineNo { get; set; }
        public DateOnly? InstallDate { get; set; }
        public string Status { get; set; } = "";
        public bool IsDemo { get; set; }
        public string Spec { get; set; } = "{}";        // jsonb ::text
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public async Task<(List<DeviceItem> Items, int Total)> ListAsync(
        DeviceListQuery query, CancellationToken ct = default)
    {
        const string FilterClause = """
             WHERE (@status::text IS NULL OR m.status = @status)
               AND (@brand::text IS NULL OR m.brand = @brand)
               AND (@q::text IS NULL OR m.asset_no ILIKE @q OR m.name ILIKE @q OR m.model ILIKE @q)
            """;
        var p = new DynamicParameters();
        p.Add("status", query.Status);
        p.Add("brand", query.Brand);
        p.Add("q", query.Q is null ? null : $"%{query.Q}%");
        p.Add("limit", query.Limit);
        p.Add("offset", query.Offset);

        var countSql = $"SELECT count(*) FROM ops.machines m {FilterClause}";
        var listSql = $$"""
            SELECT m.id, m.asset_no AS AssetNo, m.name, m.brand, m.model, m.controller,
                   m.workshop, m.line_no AS LineNo, m.install_date AS InstallDate, m.status,
                   m.is_demo AS IsDemo, m.spec::text AS Spec,
                   m.created_at AS CreatedAt, m.updated_at AS UpdatedAt
              FROM ops.machines m
            {{FilterClause}}
             ORDER BY m.asset_no ASC
             LIMIT @limit OFFSET @offset
            """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, p, cancellationToken: ct));
        var rows = await conn.QueryAsync<DeviceRow>(new CommandDefinition(listSql, p, cancellationToken: ct));
        return (rows.Select(ToItem).ToList(), total);
    }

    public async Task<long> CreateAsync(CreateDeviceRequest req, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ops.machines (
                asset_no, name, brand, model, controller, workshop, line_no,
                install_date, status, is_demo, spec
            ) VALUES (
                @assetNo, @name, @brand, @model, @controller, @workshop, @lineNo,
                @installDate, @status, @isDemo, @spec::jsonb
            )
            RETURNING id
            """;
        var p = new DynamicParameters();
        p.Add("assetNo", req.AssetNo);
        p.Add("name", req.Name);
        p.Add("brand", req.Brand);
        p.Add("model", req.Model);
        p.Add("controller", req.Controller);
        p.Add("workshop", req.Workshop);
        p.Add("lineNo", req.LineNo);
        p.Add("installDate", req.InstallDate);
        p.Add("status", req.Status);
        p.Add("isDemo", req.IsDemo);
        // spec 为 null 时写空对象 {}，否则 JSON 序列化
        p.Add("spec", req.Spec is null ? "{}" : JsonSerializer.Serialize(req.Spec));

        await using var conn = await _ds.OpenConnectionAsync(ct);
        try
        {
            return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new DeviceConflictException($"设备编号 '{req.AssetNo}' 已存在");
        }
    }

    public async Task<bool> UpdateAsync(long id, UpdateDeviceRequest req, CancellationToken ct = default)
    {
        // COALESCE 语义：传 null/缺省则保持原值。
        // 可空参数显式带类型 cast：Npgsql 不猜 null 类型。
        const string sql = """
            UPDATE ops.machines
               SET name = COALESCE(@name::text, name),
                   brand = COALESCE(@brand::text, brand),
                   model = COALESCE(@model::text, model),
                   controller = COALESCE(@controller::text, controller),
                   workshop = COALESCE(@workshop::text, workshop),
                   line_no = COALESCE(@lineNo::text, line_no),
                   install_date = COALESCE(@installDate::date, install_date),
                   status = COALESCE(@status::text, status),
                   is_demo = COALESCE(@isDemo::boolean, is_demo),
                   spec = COALESCE(@spec::jsonb, spec),
                   updated_at = now()
             WHERE id = @id
            RETURNING id
            """;
        var p = new DynamicParameters();
        p.Add("name", req.Name);
        p.Add("brand", req.Brand);
        p.Add("model", req.Model);
        p.Add("controller", req.Controller);
        p.Add("workshop", req.Workshop);
        p.Add("lineNo", req.LineNo);
        p.Add("installDate", req.InstallDate);
        p.Add("status", req.Status);
        p.Add("isDemo", req.IsDemo);
        p.Add("spec", req.Spec is null ? null : JsonSerializer.Serialize(req.Spec));
        p.Add("id", id);

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var updated = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, p, cancellationToken: ct));
        return updated.HasValue;
    }

    public async Task<int> CountWorkordersAsync(long id, CancellationToken ct = default)
    {
        const string sql = "SELECT count(*) FROM ops.maintenance_logs WHERE machine_id = @machineId";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { machineId = id }, cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM ops.machines WHERE id = @id RETURNING id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id }, cancellationToken: ct));
        return deleted.HasValue;
    }

    private static DeviceItem ToItem(DeviceRow r) => new(
        r.Id, r.AssetNo, r.Name, r.Brand, r.Model, r.Controller,
        r.Workshop, r.LineNo, r.InstallDate, r.Status, r.IsDemo,
        ParseSpec(r.Spec), r.CreatedAt, r.UpdatedAt);

    private static Dictionary<string, object?> ParseSpec(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(raw) ?? new();
        }
        catch
        {
            return new();   // JSONB 损坏时降级为空对象，不阻塞列表
        }
    }
}
