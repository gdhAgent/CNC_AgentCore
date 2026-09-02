// Application/Agent/Tools/QueryDeviceHistoryTool.cs —— 工具 3（工单聚合）
using Dapper;

namespace CNC_AgentCore.Application.Agent.Tools;

public sealed class QueryDeviceHistoryTool : IToolHandler
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;

    public QueryDeviceHistoryTool(Npgsql.NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public ToolSpec Spec { get; } = new(
        name: "query_device_history",
        description: "查询某台设备（或某报警码）近 N 天的维修工单记录，返回工单数、"
                   + "故障类型/报警码分布与最近工单。当用户问\"这台机器以前有没有报过这个警\"、"
                   + "\"3号机最近老出问题\"等历史问题时使用。",
        parameters: new Dictionary<string, object>
        {
            ["asset_no"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "机台资产编号（可选），如 CN-003",
            },
            ["alarm_code"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "报警码（可选），如 SV0401",
            },
            ["days"] = new Dictionary<string, object>
            {
                ["type"] = "integer",
                ["description"] = "统计天数（默认 90，1~3650）",
            },
        });

    private const string SqlTemplate = """
        SELECT m.asset_no, m.name AS machine_name, m.brand, m.model, m.controller,
               ml.id, ml.order_no, ml.alarm_code, ml.fault_type, ml.symptom,
               ml.root_cause, ml.action_taken, ml.engineer, ml.downtime_min,
               ml.started_at
          FROM ops.maintenance_logs ml
          JOIN ops.machines m ON m.id = ml.machine_id
         WHERE ml.started_at >= now() - make_interval(days => @days)
           {EXTRA}
         ORDER BY ml.started_at DESC
        """;

    public async Task<(string Output, Dictionary<string, object?>? Structured)> ExecuteAsync(
        IReadOnlyDictionary<string, object?> args, CancellationToken ct)
    {
        var assetNo = args.TryGetValue("asset_no", out var a) ? a?.ToString()?.Trim() : null;
        var alarmCode = args.TryGetValue("alarm_code", out var ac) ? ac?.ToString()?.Trim().ToUpperInvariant() : null;
        var days = 90;
        if (args.TryGetValue("days", out var d) && d is not null)
        {
            if (!int.TryParse(d.ToString(), out var parsed))
                throw new ArgumentException($"query_device_history: days 非法: {d}");
            days = parsed;
        }
        days = Math.Clamp(days, 1, 3650);

        var extra = new List<string>();
        var p = new DynamicParameters();
        p.Add("days", days);
        if (!string.IsNullOrWhiteSpace(assetNo)) { extra.Add("AND m.asset_no = @assetNo"); p.Add("assetNo", assetNo); }
        if (!string.IsNullOrWhiteSpace(alarmCode)) { extra.Add("AND ml.alarm_code = @alarmCode"); p.Add("alarmCode", alarmCode); }

        var sql = SqlTemplate.Replace("{EXTRA}", string.Join("\n          ", extra));

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<dynamic>(new CommandDefinition(sql, p, cancellationToken: ct))).ToList();

        var structured = new Dictionary<string, object?>
        {
            ["days"] = days,
            ["total"] = rows.Count,
            ["by_fault_type"] = new Dictionary<string, int>(),
            ["by_alarm"] = new Dictionary<string, int>(),
            ["recent"] = new List<Dictionary<string, object?>>(),
        };

        if (rows.Count == 0)
            return ($"近 {days} 天内没有匹配的维修工单记录。", structured);

        var byFault = new Dictionary<string, int>();
        var byAlarm = new Dictionary<string, int>();
        var recent = new List<Dictionary<string, object?>>();

        foreach (var r in rows)
        {
            string ft = r.fault_type?.ToString() ?? "未分类";
            byFault[ft] = byFault.GetValueOrDefault(ft, 0) + 1;
            string ac2 = r.alarm_code?.ToString() ?? "无报警码";
            byAlarm[ac2] = byAlarm.GetValueOrDefault(ac2, 0) + 1;
            if (recent.Count < 3)
            {
                recent.Add(new Dictionary<string, object?>
                {
                    ["order_no"] = r.order_no?.ToString(),
                    ["started_at"] = r.started_at?.ToString(),
                });
            }
        }
        structured["by_fault_type"] = byFault;
        structured["by_alarm"] = byAlarm;
        structured["recent"] = recent;

        var assets = rows.Select(r => r.asset_no?.ToString()).Where(s => s != null).Distinct().ToList();
        string header;
        if (assets.Count == 1)
        {
            var a0 = rows[0];
            var machine = $"{a0.machine_name}, {a0.brand} {a0.model}".Trim().TrimEnd(',');
            header = $"设备 {assets[0]}（{machine}）近 {days} 天维修：共 {rows.Count} 条";
        }
        else
        {
            header = $"近 {days} 天维修工单：共 {rows.Count} 条";
        }

        var lines = new List<string> { header };
        if (byFault.Count > 0)
            lines.Add("故障类型分布：" + string.Join("；", byFault.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));
        if (byAlarm.Count > 0)
            lines.Add("报警码分布：" + string.Join("；", byAlarm.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));
        lines.Add("最近工单：");
        foreach (var r in rows.Take(3))
        {
            var started = r.started_at is DateTime dt ? dt.ToString("yyyy-MM-dd") : r.started_at?.ToString() ?? "";
            lines.Add($"- {r.order_no}（{started}）{r.alarm_code ?? "无报警码"} / {r.fault_type ?? "未分类"}，停机 {r.downtime_min ?? 0} 分钟");
            if (!string.IsNullOrEmpty(r.symptom?.ToString())) lines.Add($"    现象：{r.symptom}");
            if (!string.IsNullOrEmpty(r.action_taken?.ToString())) lines.Add($"    处置：{r.action_taken}");
        }
        return (string.Join("\n", lines), structured);
    }
}
