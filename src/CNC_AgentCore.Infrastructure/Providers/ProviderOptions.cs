// Infrastructure/Providers/ProviderOptions.cs —— LLM/Embedding/Rerank 配置类
// env 键为扁平大写（如 DEEPSEEK_API_KEY），Load 用 IConfiguration 显式按键读取。
using Microsoft.Extensions.Configuration;

namespace CNC_AgentCore.Infrastructure.Providers;

public sealed class DeepSeekOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-chat";
    public double TimeoutSec { get; set; } = 60.0;

    public static DeepSeekOptions Load(IConfiguration cfg) => new()
    {
        ApiKey = cfg["DEEPSEEK_API_KEY"] ?? string.Empty,
        BaseUrl = cfg["DEEPSEEK_BASE_URL"] ?? "https://api.deepseek.com",
        Model = cfg["DEEPSEEK_MODEL"] ?? "deepseek-chat",
    };
}

public sealed class SiliconFlowOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.siliconflow.cn/v1";
    public string EmbeddingModel { get; set; } = "BAAI/bge-m3";
    public int EmbeddingDim { get; set; } = 1024;
    public string RerankModel { get; set; } = "BAAI/bge-reranker-v2-m3";
    public double TimeoutSec { get; set; } = 60.0;

    public static SiliconFlowOptions Load(IConfiguration cfg) => new()
    {
        ApiKey = cfg["SILICONFLOW_API_KEY"] ?? string.Empty,
        BaseUrl = cfg["SILICONFLOW_BASE_URL"] ?? "https://api.siliconflow.cn/v1",
        EmbeddingModel = cfg["EMBEDDING_MODEL"] ?? "BAAI/bge-m3",
        EmbeddingDim = int.TryParse(cfg["EMBEDDING_DIM"], out var d) ? d : 1024,
        RerankModel = cfg["RERANK_MODEL"] ?? "BAAI/bge-reranker-v2-m3",
    };
}
