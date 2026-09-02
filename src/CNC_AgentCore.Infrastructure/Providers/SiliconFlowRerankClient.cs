// Infrastructure/Providers/SiliconFlowRerankClient.cs —— 硅基流动 Rerank（直接调 /v1/rerank HTTP）
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CNC_AgentCore.Domain.Abstractions;

namespace CNC_AgentCore.Infrastructure.Providers;

public sealed class SiliconFlowRerankClient : IRerankClient
{
    private readonly HttpClient _http;
    private readonly SiliconFlowOptions _opts;

    public SiliconFlowRerankClient(SiliconFlowOptions opts, HttpMessageHandler? handler = null)
    {
        _opts = opts;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new ArgumentException("SiliconFlow api_key 未配置", nameof(opts));

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSec);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
    }

    public async Task<IReadOnlyList<(int OriginalIndex, double Score)>> RerankAsync(
        string query, IReadOnlyList<string> documents, int? topN = null, CancellationToken ct = default)
    {
        if (documents.Count == 0) return Array.Empty<(int, double)>();

        var body = new RerankRequest
        {
            Model = _opts.RerankModel,
            Query = query,
            Documents = documents.ToArray(),
            TopN = topN ?? documents.Count,
            ReturnDocuments = false,
        };
        var url = _opts.BaseUrl.TrimEnd('/') + "/rerank";

        using var resp = await _http.PostAsJsonAsync(url, body, ct);
        resp.EnsureSuccessStatusCode();
        var parsed = await resp.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Rerank 响应为空");

        var pairs = new List<(int OriginalIndex, double Score)>(parsed.Results?.Count ?? 0);
        foreach (var r in parsed.Results ?? Enumerable.Empty<RerankItem>())
            pairs.Add((r.Index, r.Score));
        pairs.Sort((a, b) => b.Score.CompareTo(a.Score));   // score 降序
        if (topN is not null && pairs.Count > topN.Value) pairs = pairs.GetRange(0, topN.Value);
        return pairs;
    }

    private sealed class RerankRequest
    {
        public string Model { get; set; } = "";
        public string Query { get; set; } = "";
        public string[] Documents { get; set; } = Array.Empty<string>();
        [JsonPropertyName("top_n")]
        public int TopN { get; set; }
        [JsonPropertyName("return_documents")]
        public bool ReturnDocuments { get; set; }
    }

    private sealed class RerankResponse
    {
        public List<RerankItem>? Results { get; set; }
    }

    private sealed class RerankItem
    {
        public int Index { get; set; }
        [JsonPropertyName("relevance_score")]
        public double Score { get; set; }
    }
}
