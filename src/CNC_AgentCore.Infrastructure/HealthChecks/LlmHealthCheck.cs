// Infrastructure/HealthChecks/LlmHealthCheck.cs —— LLM 健康检查
// 缺 key → Healthy(skipped)（不影响 overall）；真错 → Unhealthy。用 IServiceProvider 延迟解析 IChatClient，避免缺 key 时构造即抛。
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CNC_AgentCore.Infrastructure.Providers;

namespace CNC_AgentCore.Infrastructure.HealthChecks;

public sealed class LlmHealthCheck : IHealthCheck
{
    private readonly DeepSeekOptions _opts;
    private readonly IServiceProvider _sp;

    public LlmHealthCheck(DeepSeekOptions opts, IServiceProvider sp)
    {
        _opts = opts;
        _sp = sp;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object> { ["model"] = _opts.Model };
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            data["status"] = "skipped";
            data["error"] = "DeepSeek api_key 未配置";
            return HealthCheckResult.Healthy(data: data);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var chat = _sp.GetRequiredService<IChatClient>();
            var resp = await chat.GetResponseAsync(
                "只回 pong 一个词",
                new ChatOptions { Temperature = 0f, MaxOutputTokens = 8 },
                ct);
            var text = resp.Text?.Trim() ?? string.Empty;
            data["status"] = "ok";
            data["preview"] = text.Length > 32 ? text[..32] : text;
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
