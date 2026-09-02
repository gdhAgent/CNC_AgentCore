// Application/WorkOrders/WorkOrderRepository.cs —— 工单与设备台账 Dapper 数据访问。
using System.Text.Json;
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.WorkOrders;

public sealed class WorkOrderRepository : IWorkOrderRepository
{
    private readonly NpgsqlDataSource _ds;

    public WorkOrderRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<bool> MachineExistsAsync(long machineId, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM ops.machines WHERE id = @machineId)",
            new { machineId }, cancellationToken: ct));
    }

    public async Task<long> InsertAsync(CreateWorkorderRequest r, CancellationToken ct = default)
    {
        // started_at 缺省走 now()；parts_used 以 jsonb 强转
        const string sql = """
            INSERT INTO ops.maintenance_logs (
                machine_id, order_no, alarm_code, fault_type, symptom,
                root_cause, action_taken, parts_used, engineer, downtime_min,
                started_at, finished_at, is_demo
            ) VALUES (
                @machineId, @orderNo, @alarmCode, @faultType, @symptom,
                @rootCause, @actionTaken, @partsUsed::jsonb, @engineer, @downtimeMin,
                COALESCE(@startedAt, now()), @finishedAt, @isDemo
            )
            RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var p = new DynamicParameters();
        p.Add("machineId", r.MachineId);
        p.Add("orderNo", r.OrderNo);
        p.Add("alarmCode", r.AlarmCode);
        p.Add("faultType", r.FaultType);
        p.Add("symptom", r.Symptom);
        p.Add("rootCause", r.RootCause);
        p.Add("actionTaken", r.ActionTaken);
        p.Add("partsUsed", r.PartsUsed is null ? null : JsonSerializer.Serialize(r.PartsUsed));
        p.Add("engineer", r.Engineer);
        p.Add("downtimeMin", r.DowntimeMin);
        p.Add("startedAt", r.StartedAt);
        p.Add("finishedAt", r.FinishedAt);
        p.Add("isDemo", r.IsDemo);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    private sealed class VectorizeRowData
    {
        public long Id { get; set; }
        public string? AlarmCode { get; set; }
        public string? FaultType { get; set; }
        public string? Symptom { get; set; }
        public string? ActionTaken { get; set; }
        public string AssetNo { get; set; } = "";
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? Controller { get; set; }
    }

    public async Task<MaintenanceLogVectorizeRow?> FetchVectorizeRowAsync(long workorderId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ml.id, ml.alarm_code AS AlarmCode, ml.fault_type AS FaultType, ml.symptom, ml.action_taken AS ActionTaken,
                   m.asset_no AS AssetNo, m.brand, m.model, m.controller
              FROM ops.maintenance_logs ml
              JOIN ops.machines m ON m.id = ml.machine_id
             WHERE ml.id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<VectorizeRowData>(new CommandDefinition(
            sql, new { id = workorderId }, cancellationToken: ct));
        if (row is null) return null;
        return new MaintenanceLogVectorizeRow(
            row.Id, row.AlarmCode, row.FaultType, row.Symptom, row.ActionTaken,
            row.AssetNo, row.Brand, row.Model, row.Controller);
    }

    public async Task<bool> UpdateEmbeddingAsync(long workorderId, float[] vector, CancellationToken ct = default)
    {
        // float[] 写 pgvector 列：以 "[a,b,...]" 字符串 + ::vector 强转
        var vecLiteral = "[" + string.Join(",", vector.Select(v => v.ToString("G9"))) + "]";
        const string sql = """
            UPDATE ops.maintenance_logs
               SET embedding = @vec::vector
             WHERE id = @id
            RETURNING id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var updated = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id = workorderId, vec = vecLiteral }, cancellationToken: ct));
        return updated.HasValue;
    }

    // ===== 列表 =====

    private sealed class MachineRow
    {
        public long Id { get; set; }
        public string AssetNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public string? Model { get; set; }
        public string? Controller { get; set; }
        public string? Workshop { get; set; }
        public string? LineNo { get; set; }
        public string Status { get; set; } = "running";
        public bool IsDemo { get; set; }
        public long WorkorderCount { get; set; }
    }

    public async Task<List<MachineListItem>> ListMachinesAsync(int limit, int offset, CancellationToken ct = default)
    {
        const string sql = """
            SELECT m.id, m.asset_no AS AssetNo, m.name, m.brand, m.model, m.controller,
                   m.workshop, m.line_no AS LineNo, m.status, m.is_demo AS IsDemo,
                   COUNT(ml.id) AS WorkorderCount
              FROM ops.machines m
              LEFT JOIN ops.maintenance_logs ml ON ml.machine_id = m.id
             GROUP BY m.id
             ORDER BY m.asset_no ASC
             LIMIT @lim OFFSET @off
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MachineRow>(new CommandDefinition(
            sql, new { lim = limit, off = offset }, cancellationToken: ct));
        return rows.Select(r => new MachineListItem(
            r.Id, r.AssetNo, r.Name, r.Brand, r.Model, r.Controller,
            r.Workshop, r.LineNo, r.Status, r.IsDemo, r.WorkorderCount)).ToList();
    }

    private sealed class WorkorderRow
    {
        public long Id { get; set; }
        public string? OrderNo { get; set; }
        public long MachineId { get; set; }
        public string? AssetNo { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? AlarmCode { get; set; }
        public string? FaultType { get; set; }
        public string? Symptom { get; set; }
        public string? RootCause { get; set; }
        public string? ActionTaken { get; set; }
        public string? Engineer { get; set; }
        public int? DowntimeMin { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public bool IsDemo { get; set; }
        public string? AlarmName { get; set; }
        public string? AlarmSeverity { get; set; }
    }

    public async Task<(List<WorkorderListItem> Items, int Total)> ListWorkordersAsync(
        WorkorderListQuery q, CancellationToken ct = default)
    {
        // 7 个过滤条件 + 1 个 OR；用 IS NULL OR 短路避免动态 SQL 拼接
        const string filterClause = """
             WHERE (@alarmCode::text IS NULL OR ml.alarm_code = @alarmCode)
               AND (@machineId::bigint IS NULL OR ml.machine_id = @machineId)
               AND (@brand::text IS NULL OR m.brand = @brand)
               AND (@faultType::text IS NULL OR ml.fault_type = @faultType)
               AND (@fromTime::timestamptz IS NULL OR ml.started_at >= @fromTime)
               AND (@toTime::timestamptz IS NULL OR ml.started_at <= @toTime)
            """;
        var listSql = $$"""
            SELECT ml.id, ml.order_no AS OrderNo, ml.machine_id AS MachineId, m.asset_no AS AssetNo, m.brand, m.model,
                   ml.alarm_code AS AlarmCode, ml.fault_type AS FaultType, ml.symptom, ml.root_cause AS RootCause, ml.action_taken AS ActionTaken,
                   ml.engineer, ml.downtime_min AS DowntimeMin, ml.started_at AS StartedAt, ml.finished_at AS FinishedAt, ml.is_demo AS IsDemo,
                   a.name AS AlarmName, a.severity AS AlarmSeverity
              FROM ops.maintenance_logs ml
              LEFT JOIN ops.machines m ON m.id = ml.machine_id
              LEFT JOIN kb.alarms a ON a.code_norm = ml.alarm_code AND a.brand = m.brand
            {{filterClause}}
             ORDER BY ml.started_at DESC NULLS LAST, ml.id DESC
             LIMIT @lim OFFSET @off
            """;
        var countSql = $"SELECT count(*) FROM ops.maintenance_logs ml LEFT JOIN ops.machines m ON m.id = ml.machine_id {filterClause}";

        var p = new DynamicParameters();
        p.Add("alarmCode", q.AlarmCode);
        p.Add("machineId", q.MachineId);
        p.Add("brand", q.Brand);
        p.Add("faultType", q.FaultType);
        p.Add("fromTime", q.FromTime);
        p.Add("toTime", q.ToTime);
        p.Add("lim", q.Limit);
        p.Add("off", q.Offset);

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, p, cancellationToken: ct));
        var rows = await conn.QueryAsync<WorkorderRow>(new CommandDefinition(listSql, p, cancellationToken: ct));
        var items = rows.Select(r => new WorkorderListItem(
            r.Id, r.OrderNo, r.MachineId, r.AssetNo, r.Brand, r.Model,
            r.AlarmCode, r.FaultType, r.Symptom, r.RootCause, r.ActionTaken,
            r.Engineer, r.DowntimeMin, r.StartedAt, r.FinishedAt, r.IsDemo,
            r.AlarmName, r.AlarmSeverity)).ToList();
        return (items, total);
    }

    // ===== 详情 + 删除 =====

    private sealed class DetailRow
    {
        public long Id { get; set; }
        public string? OrderNo { get; set; }
        public long MachineId { get; set; }
        public string? AssetNo { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? AlarmCode { get; set; }
        public string? FaultType { get; set; }
        public string? Symptom { get; set; }
        public string? RootCause { get; set; }
        public string? ActionTaken { get; set; }
        public string PartsUsed { get; set; } = "[]";        // JSONB ::text
        public string? Engineer { get; set; }
        public int? DowntimeMin { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public bool IsDemo { get; set; }
        public string? AlarmName { get; set; }
        public string? AlarmSeverity { get; set; }
        public string? AlarmCause { get; set; }
    }

    public async Task<WorkorderDetail?> GetDetailAsync(long workorderId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ml.id, ml.order_no AS OrderNo, ml.machine_id AS MachineId, m.asset_no AS AssetNo, m.brand, m.model,
                   ml.alarm_code AS AlarmCode, ml.fault_type AS FaultType, ml.symptom, ml.root_cause AS RootCause, ml.action_taken AS ActionTaken,
                   ml.parts_used::text AS PartsUsed,
                   ml.engineer, ml.downtime_min AS DowntimeMin,
                   ml.started_at AS StartedAt, ml.finished_at AS FinishedAt, ml.is_demo AS IsDemo,
                   a.name AS AlarmName, a.severity AS AlarmSeverity, a.cause AS AlarmCause
              FROM ops.maintenance_logs ml
              LEFT JOIN ops.machines m ON m.id = ml.machine_id
              LEFT JOIN kb.alarms a ON a.code_norm = ml.alarm_code AND a.brand = m.brand
             WHERE ml.id = @id
            """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<DetailRow>(new CommandDefinition(
            sql, new { id = workorderId }, cancellationToken: ct));
        if (row is null) return null;

        var parts = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(row.PartsUsed) && row.PartsUsed != "[]")
        {
            try
            {
                parts = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(row.PartsUsed) ?? new();
            }
            catch
            {
                parts = new();
            }
        }

        return new WorkorderDetail(
            row.Id, row.OrderNo, row.MachineId, row.AssetNo, row.Brand, row.Model,
            row.AlarmCode, row.FaultType, row.Symptom, row.RootCause, row.ActionTaken,
            parts, row.Engineer, row.DowntimeMin, row.StartedAt, row.FinishedAt, row.IsDemo,
            row.AlarmName, row.AlarmSeverity, row.AlarmCause);
    }

    public async Task<bool> DeleteAsync(long workorderId, CancellationToken ct = default)
    {
        // embedding 同行删除；无子表级联负担
        const string sql = "DELETE FROM ops.maintenance_logs WHERE id = @id RETURNING id";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            sql, new { id = workorderId }, cancellationToken: ct));
        return deleted.HasValue;
    }
}
