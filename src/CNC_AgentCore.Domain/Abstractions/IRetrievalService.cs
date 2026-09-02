// Domain/Abstractions/IRetrievalService.cs —— 8 步混合检索服务
using CNC_AgentCore.Domain.ValueObjects;

namespace CNC_AgentCore.Domain.Abstractions;

public sealed class RetrievalServiceConfig
{
    public int VectorTopN { get; init; } = 30;
    public int FulltextTopN { get; init; } = 30;
    public int RrfTopN { get; init; } = 20;
    public int RerankTopN { get; init; } = 5;
    public double RerankThreshold { get; init; } = 0.30;
    public string? Brand { get; init; }
    public string? MachineModel { get; init; }
    public double TrgmThreshold { get; init; } = 0.3;
    public bool EnableTrgmFallback { get; init; } = true;
}

public interface IRetrievalService
{
    Task<QueryResult> RunQueryAsync(string queryText, RetrievalServiceConfig? cfg = null, CancellationToken ct = default);
}
