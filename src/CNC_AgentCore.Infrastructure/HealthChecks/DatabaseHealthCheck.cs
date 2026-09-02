// Infrastructure/HealthChecks/DatabaseHealthCheck.cs —— DB 健康检查：SELECT 1, current_database() 探活
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CNC_AgentCore.Infrastructure.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _ds;

    public DatabaseHealthCheck(NpgsqlDataSource ds) => _ds = ds;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var conn = await _ds.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1, current_database()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return HealthCheckResult.Unhealthy(description: "SELECT 1 无返回行");

            var one = reader.GetInt32(0);
            var dbname = reader.GetString(1);
            if (one != 1)
                return HealthCheckResult.Unhealthy(description: $"unexpected SELECT 1 result: {one}");

            sw.Stop();
            return HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                ["status"] = "ok",
                ["dbname"] = dbname,
                ["ms"] = sw.ElapsedMilliseconds,
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return HealthCheckResult.Unhealthy(description: ex.Message, exception: ex, data: new Dictionary<string, object>
            {
                ["status"] = "down",
                ["ms"] = sw.ElapsedMilliseconds,
            });
        }
    }
}
