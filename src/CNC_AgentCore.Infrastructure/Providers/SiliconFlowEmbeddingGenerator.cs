// Infrastructure/Providers/SiliconFlowEmbeddingGenerator.cs —— 硅基流动 Embedding
// 直调 /v1/embeddings HTTP（OpenAI 兼容），实现 IEmbeddingGenerator<string, Embedding<float>>，业务统一走 MEAI。
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace CNC_AgentCore.Infrastructure.Providers;

public sealed class SiliconFlowEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _http;
    private readonly SiliconFlowOptions _opts;

    public SiliconFlowEmbeddingGenerator(SiliconFlowOptions opts, HttpMessageHandler? handler = null)
    {
        _opts = opts;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new ArgumentException("SiliconFlow api_key 未配置", nameof(opts));

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSec);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
    }

    public async Task<Embedding<float>> GenerateAsync(
        string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var results = await GenerateAsync(new[] { value }, options, cancellationToken);
        return results[0];
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var texts = values.ToArray();
        if (texts.Length == 0) return new GeneratedEmbeddings<Embedding<float>>();

        var body = new EmbeddingRequest
        {
            Model = _opts.EmbeddingModel,
            Input = texts,
            EncodingFormat = "float",
        };
        var url = _opts.BaseUrl.TrimEnd('/') + "/embeddings";

        using var resp = await _http.PostAsJsonAsync(url, body, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var parsed = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Embedding 响应为空");

        var items = parsed.Data ?? new List<EmbeddingItem>();
        items.Sort((a, b) => a.Index.CompareTo(b.Index));   // 按 index 排序保证与 inputs 顺序一致

        var vectors = new Embedding<float>[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var v = items[i].Embedding;
            if (v is null || v.Length != _opts.EmbeddingDim)
                throw new InvalidOperationException(
                    $"Embedding dim 不匹配 text[{i}]：got {(v?.Length ?? 0)}，expected {_opts.EmbeddingDim}");
            vectors[i] = new Embedding<float>(v);
        }
        return new GeneratedEmbeddings<Embedding<float>>(vectors);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => _http.Dispose();

    private sealed class EmbeddingRequest
    {
        public string Model { get; set; } = "";
        public string[] Input { get; set; } = Array.Empty<string>();
        [JsonPropertyName("encoding_format")]
        public string EncodingFormat { get; set; } = "float";
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingItem>? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        public float[]? Embedding { get; set; }
        public int Index { get; set; }
    }
}
