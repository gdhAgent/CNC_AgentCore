// Infrastructure/HealthChecks/EmbeddingHealthCheck.cs —— Embedding 健康检查：单条中文，校验返回维度
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CNC_AgentCore.Infrastructure.Providers;

namespace CNC_AgentCore.Infrastructure.HealthChecks;

public sealed class EmbeddingHealthCheck : IHealthCheck
{
    private readonly SiliconFlowOptions _opts;
    private readonly IServiceProvider _sp;

    public EmbeddingHealthCheck(SiliconFlowOptions opts, IServiceProvider sp)
    {
        _opts = opts;
        _sp = sp;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>
        {
            ["model"] = _opts.EmbeddingModel,
            ["expected_dim"] = _opts.EmbeddingDim,
        };
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            data["status"] = "skipped";
            data["error"] = "SiliconFlow api_key 未配置";
            return HealthCheckResult.Healthy(data: data);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var gen = _sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var vectors = await gen.GenerateAsync(new[] { "主轴伺服放大器报警 SV0401" }, cancellationToken: ct);
            if (vectors.Count > 0 && vectors[0].Vector.Length == _opts.EmbeddingDim)
            {
                data["status"] = "ok";
                data["got_dim"] = vectors[0].Vector.Length;
            }
            else
            {
                var got = vectors.Count > 0 ? vectors[0].Vector.Length : 0;
                sw.Stop();
                data["status"] = "down";
                data["error"] = $"dim mismatch, got {got}";
                data["ms"] = sw.ElapsedMilliseconds;
                return HealthCheckResult.Unhealthy(description: $"embedding dim mismatch, got {got}", data: data);
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
