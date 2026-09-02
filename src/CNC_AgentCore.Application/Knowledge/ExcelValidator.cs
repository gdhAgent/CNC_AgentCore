// Application/Knowledge/ExcelValidator.cs —— Excel 行级校验；输出 (validRows, dupRows, errorRows, errors[])。
using Dapper;
using CNC_AgentCore.Domain.Abstractions;
using Npgsql;

namespace CNC_AgentCore.Application.Knowledge;

public sealed record ExcelValidationError(int Row, string Field, string Reason);

public sealed record ExcelValidationResult(
    int TotalRows,
    int ValidRows,
    int DupRows,
    int ErrorRows,
    List<ExcelValidationError> Errors);

public static class ExcelValidator
{
    public static readonly HashSet<string> ValidSeverities = new()
    {
        "info", "warning", "fault", "fatal", "unknown",
    };
    public static readonly HashSet<string> ValidCategories = new()
    {
        "伺服", "主轴", "PMC", "超程", "程序错误", "液压", "气动", "电气", "机械", "其他",
    };
    public static readonly HashSet<string> ValidMachineStatuses = new()
    {
        "running", "idle", "repair", "scrapped",
    };

    /// <summary>校验导入行（按 jobType）。返回 (validRows, dupRows, errorRows, errors)。</summary>
    public static async Task<ExcelValidationResult> ValidateAsync(
        NpgsqlDataSource ds, string jobType, List<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        var errors = new List<ExcelValidationError>();
        var validKeys = new List<Dictionary<string, string>>();

        for (var i = 0; i < rows.Count; i++)
        {
            var rowNum = i + 4;  // 模板约定数据从第 4 行开始
            var row = rows[i];
            var rowErrors = jobType switch
            {
                "alarm" => ValidateAlarmRow(row),
                "faq" => ValidateFaqRow(row),
                "machine" => ValidateMachineRow(row),
                "maintenance" => ValidateMaintenanceRow(row),
                _ => new List<ExcelValidationError> { new(0, "type", $"未知 job_type={jobType}") },
            };
            if (rowErrors.Count == 0)
                validKeys.Add(row);
            else
                errors.AddRange(rowErrors.Select(e => e with { Row = rowNum }));
        }

        // 重复检测：同表内 (brand, controller, code) 或 (title) 唯一
        var dups = await DetectDuplicatesAsync(ds, jobType, validKeys, ct);

        return new ExcelValidationResult(
            TotalRows: rows.Count,
            ValidRows: validKeys.Count - dups,
            DupRows: dups,
            ErrorRows: errors.Count,
            Errors: errors);
    }

    private static List<ExcelValidationError> ValidateAlarmRow(Dictionary<string, string> r)
    {
        var errs = new List<ExcelValidationError>();
        var brand = r.GetValueOrDefault("brand", "").Trim();
        var code = r.GetValueOrDefault("code", "").Trim();
        var name = r.GetValueOrDefault("name", "").Trim();
        var severity = r.GetValueOrDefault("severity", "unknown").Trim();
        var category = r.GetValueOrDefault("category", "").Trim();

        if (string.IsNullOrEmpty(brand)) errs.Add(new(0, "brand", "必填"));
        if (string.IsNullOrEmpty(code)) errs.Add(new(0, "code", "必填"));
        if (string.IsNullOrEmpty(name)) errs.Add(new(0, "name", "必填"));
        if (!string.IsNullOrEmpty(severity) && !ValidSeverities.Contains(severity))
            errs.Add(new(0, "severity", $"必须是 {string.Join('/', ValidSeverities)}"));
        if (!string.IsNullOrEmpty(category) && !ValidCategories.Contains(category))
            errs.Add(new(0, "category", $"必须是 {string.Join('/', ValidCategories)}"));
        return errs;
    }

    private static List<ExcelValidationError> ValidateFaqRow(Dictionary<string, string> r)
    {
        var errs = new List<ExcelValidationError>();
        if (string.IsNullOrEmpty(r.GetValueOrDefault("title", "").Trim()))
            errs.Add(new(0, "title", "必填"));
        if (string.IsNullOrEmpty(r.GetValueOrDefault("body", "").Trim()))
            errs.Add(new(0, "body", "必填"));
        return errs;
    }

    private static List<ExcelValidationError> ValidateMachineRow(Dictionary<string, string> r)
    {
        var errs = new List<ExcelValidationError>();
        if (string.IsNullOrEmpty(r.GetValueOrDefault("asset_no", "").Trim()))
            errs.Add(new(0, "asset_no", "必填"));
        if (string.IsNullOrEmpty(r.GetValueOrDefault("name", "").Trim()))
            errs.Add(new(0, "name", "必填"));
        if (string.IsNullOrEmpty(r.GetValueOrDefault("brand", "").Trim()))
            errs.Add(new(0, "brand", "必填"));
        var status = r.GetValueOrDefault("status", "running").Trim();
        if (!string.IsNullOrEmpty(status) && !ValidMachineStatuses.Contains(status))
            errs.Add(new(0, "status", $"必须是 {string.Join('/', ValidMachineStatuses)}"));
        return errs;
    }

    private static List<ExcelValidationError> ValidateMaintenanceRow(Dictionary<string, string> r)
    {
        var errs = new List<ExcelValidationError>();
        if (!long.TryParse(r.GetValueOrDefault("machine_id", "").Trim(), out _))
            errs.Add(new(0, "machine_id", "必须是数字"));
        if (string.IsNullOrEmpty(r.GetValueOrDefault("symptom", "").Trim()))
            errs.Add(new(0, "symptom", "必填"));
        return errs;
    }

    private static async Task<int> DetectDuplicatesAsync(
        NpgsqlDataSource ds, string jobType, List<Dictionary<string, string>> validRows, CancellationToken ct)
    {
        if (validRows.Count == 0) return 0;
        await using var conn = await ds.OpenConnectionAsync(ct);

        if (jobType == "alarm")
        {
            // 与库内已有的 (brand, controller, code_norm) 重复
            var codes = validRows
                .Select(r => (r.GetValueOrDefault("brand", ""), r.GetValueOrDefault("controller", ""), r.GetValueOrDefault("code", "")))
                .ToList();
            var dups = 0;
            foreach (var (brand, controller, code) in codes)
            {
                if (string.IsNullOrEmpty(brand) || string.IsNullOrEmpty(code)) continue;
                var codeNorm = code.TrimStart('0').ToUpperInvariant();
                if (string.IsNullOrEmpty(codeNorm)) codeNorm = code.ToUpperInvariant();
                var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*) FROM kb.alarms WHERE brand=@b AND COALESCE(controller,'')=@c AND code_norm=@n",
                    new { b = brand, c = controller ?? "", n = codeNorm }, cancellationToken: ct));
                if (exists > 0) dups++;
            }
            return dups;
        }
        if (jobType == "faq")
        {
            var titles = validRows.Select(r => r.GetValueOrDefault("title", "")).ToList();
            var dups = 0;
            foreach (var t in titles)
            {
                if (string.IsNullOrEmpty(t)) continue;
                var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*) FROM kb.documents WHERE title=@t AND doc_type='faq'",
                    new { t }, cancellationToken: ct));
                if (exists > 0) dups++;
            }
            return dups;
        }
        if (jobType == "machine")
        {
            var assetNos = validRows.Select(r => r.GetValueOrDefault("asset_no", "")).ToList();
            var dups = 0;
            foreach (var a in assetNos)
            {
                if (string.IsNullOrEmpty(a)) continue;
                var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*) FROM ops.machines WHERE asset_no=@a",
                    new { a }, cancellationToken: ct));
                if (exists > 0) dups++;
            }
            return dups;
        }
        return 0;
    }
}
