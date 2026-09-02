// Infrastructure/HealthChecks/RerankHealthCheck.cs —— Rerank 健康检查：2 条文档，验证按 score 降序返回
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Infrastructure.Providers;

namespace CNC_AgentCore.Infrastructure.HealthChecks;

public sealed class RerankHealthCheck : IHealthCheck
{
    private readonly SiliconFlowOptions _opts;
    private readonly IServiceProvider _sp;

    public RerankHealthCheck(SiliconFlowOptions opts, IServiceProvider sp)
    {
        _opts = opts;
        _sp = sp;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object> { ["model"] = _opts.RerankModel };
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            data["status"] = "skipped";
            data["error"] = "SiliconFlow api_key 未配置";
            return HealthCheckResult.Healthy(data: data);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var rerank = _sp.GetRequiredService<IRerankClient>();
            var pairs = await rerank.RerankAsync(
                "FANUC 主轴伺服报警 SV0401",
                new[] { "SV0401 是速度就绪信号断开", "今天中午吃什么外卖" },
                ct: ct);
            if (pairs.Count == 2 && pairs[0].Score >= pairs[1].Score)
            {
                data["status"] = "ok";
                data["top_score"] = pairs[0].Score;
            }
            else
            {
                sw.Stop();
                data["status"] = "down";
                data["error"] = $"unexpected rerank order: count={pairs.Count}";
                data["ms"] = sw.ElapsedMilliseconds;
                return HealthCheckResult.Unhealthy(description: "unexpected rerank order", data: data);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            data["status"] = "down";
            data["error"] = ex.Message;
            data["ms"] = sw.ElapsedMilliseconds;
            return HealthCheckResult.Unhealthy(description: ex.Message, exception: ex, data: data);
        }
        sw.Stop();
        data["ms"] = sw.ElapsedMilliseconds;
        return HealthCheckResult.Healthy(data: data);
    }
}
