// Application/Knowledge/ExcelTemplates.cs —— Excel 模板生成、解析与导出（ClosedXML）。
using System.Globalization;
using ClosedXML.Excel;

namespace CNC_AgentCore.Application.Knowledge;

public static class ExcelTemplates
{
    // ===== Alarm 模板字段顺序 =====
    public static readonly string[] AlarmHeaders = new[]
    {
        "brand", "controller", "code", "name", "category", "severity",
        "description", "cause", "action", "safety_note",
    };
    public static readonly string[] AlarmExample = new[]
    {
        "FANUC", "0i-MF", "SV0401", "伺服 V-Ready 信号关闭",
        "伺服", "fault",
        "伺服放大器就绪信号断开", "伺服放大器电源异常", "检查电源并复位", "⚠️ 检查伺服放大器前必须断电并等待 5 分钟放电",
    };

    public static readonly string[] FaqHeaders = new[]
    {
        "title", "body", "brand", "model_scope",
    };
    public static readonly string[] FaqExample = new[]
    {
        "进给倍率异常排查", "检查 G95/G94 模态是否切换正确；模态不一致时实际进给 F 值与代码差 100 倍。",
        "FANUC", "VMC850",
    };

    public static readonly string[] MachineHeaders = new[]
    {
        "asset_no", "name", "brand", "model", "controller", "workshop", "line_no", "status",
    };
    public static readonly string[] MachineExample = new[]
    {
        "E1024", "立式加工中心-03", "FANUC", "VMC850", "FANUC 0i-MF", "一车间", "线1", "running",
    };

    public static readonly string[] MaintenanceHeaders = new[]
    {
        "machine_id", "order_no", "alarm_code", "fault_type", "symptom", "action_taken", "engineer", "downtime_min",
    };
    public static readonly string[] MaintenanceExample = new[]
    {
        "1", "WO-2026-001", "SV0401", "电气", "主轴伺服报警", "检查伺服电源后复位", "E1024", "60",
    };

    /// <summary>生成模板 xlsx 二进制（ClosedXML 流式写出）。</summary>
    public static byte[] GenerateTemplateBytes(string jobType)
    {
        using var wb = new XLWorkbook();
        var (headers, example, sheetName) = jobType switch
        {
            "alarm" => (AlarmHeaders, AlarmExample, "alarms"),
            "faq" => (FaqHeaders, FaqExample, "faqs"),
            "machine" => (MachineHeaders, MachineExample, "machines"),
            "maintenance" => (MaintenanceHeaders, MaintenanceExample, "maintenance_logs"),
            _ => throw new ArgumentException($"未知 job_type={jobType}"),
        };

        var ws = wb.AddWorksheet(sheetName);

        // 行 1：字段说明（人类可读）
        ws.Cell(1, 1).Value = "# Excel 模板说明：";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "# 第 1 行为字段名（导入时按表头取）；第 2 行为示例数据（导入前请删除）。";

        // 行 3：表头
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(3, i + 1).Value = headers[i];
            ws.Cell(3, i + 1).Style.Font.Bold = true;
        }

        // 行 4：示例
        for (var i = 0; i < example.Length; i++)
        {
            ws.Cell(4, i + 1).Value = example[i];
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>解析 xlsx 字节流为行字典列表（每行 → Dictionary&lt;列名, 值&gt;）。</summary>
    public static List<Dictionary<string, string>> ParseXlsx(byte[] bytes)
    {
        var rows = new List<Dictionary<string, string>>();
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheets.First();
        var range = ws.RangeUsed();
        if (range is null) return rows;

        var headerRow = ws.Row(3);   // 我们模板约定第 3 行为表头
        var headers = new List<string>();
        foreach (var cell in headerRow.Cells())
            headers.Add(cell.GetString().Trim());

        // 数据行从第 4 行开始
        for (var r = 4; r <= range.LastRow().RowNumber(); r++)
        {
            var row = ws.Row(r);
            var dict = new Dictionary<string, string>();
            var hasValue = false;
            for (var c = 1; c <= headers.Count; c++)
            {
                var val = row.Cell(c).GetString().Trim();
                if (!string.IsNullOrEmpty(val)) hasValue = true;
                if (headers[c - 1] != "")
                    dict[headers[c - 1]] = val;
            }
            if (hasValue) rows.Add(dict);
        }
        return rows;
    }

    /// <summary>按 jobType 导出对应表数据为 xlsx。</summary>
    public static byte[] ExportToXlsx(string jobType, IEnumerable<Dictionary<string, string>> rows)
    {
        using var wb = new XLWorkbook();
        var (headers, sheetName) = jobType switch
        {
            "alarm" => (AlarmHeaders, "alarms"),
            "faq" => (FaqHeaders, "faqs"),
            "machine" => (MachineHeaders, "machines"),
            "maintenance" => (MaintenanceHeaders, "maintenance_logs"),
            _ => throw new ArgumentException($"未知 job_type={jobType}"),
        };

        var ws = wb.AddWorksheet(sheetName);
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        var rowIdx = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < headers.Length; i++)
            {
                if (row.TryGetValue(headers[i], out var v))
                ws.Cell(rowIdx, i + 1).Value = v;
            }
            rowIdx++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>把 import_jobs.errors JSON 序列转 CSV 字节（errors.csv）。</summary>
    public static byte[] ErrorsToCsv(string errorsJson)
    {
        var lines = new List<string> { "row,field,reason" };
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(errorsJson);
            if (arr is null) return System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
            foreach (var e in arr)
            {
                var row = e.TryGetValue("Row", out var r) ? r?.ToString() ?? "" : "";
                var field = e.TryGetValue("Field", out var f) ? f?.ToString() ?? "" : "";
                var reason = e.TryGetValue("Reason", out var re) ? re?.ToString() ?? "" : "";
                lines.Add($"{row},{field},{reason}");
            }
        }
        catch { }
        return System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    /// <summary>把 import_jobs.errors JSON 序列转 xlsx 字节（errors.xlsx）。</summary>
    public static byte[] ErrorsToXlsx(string errorsJson)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("errors");

        // 表头 row 1：row | field | reason（bold），数据从第 2 行
        var headers = new[] { "row", "field", "reason" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        // 读键兼容 PascalCase（.NET 匿名对象序列化默认）与小写两种写法
        static string ReadKey(Dictionary<string, object?> e, params string[] names)
        {
            foreach (var n in names)
                if (e.TryGetValue(n, out var v))
                    return v?.ToString() ?? "";
            return "";
        }

        var rowIdx = 2;
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(errorsJson);
            if (arr is not null)
            {
                foreach (var e in arr)
                {
                    ws.Cell(rowIdx, 1).Value = ReadKey(e, "Row", "row");
                    ws.Cell(rowIdx, 2).Value = ReadKey(e, "Field", "field");
                    ws.Cell(rowIdx, 3).Value = ReadKey(e, "Reason", "reason");
                    rowIdx++;
                }
            }
        }
        catch { }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
