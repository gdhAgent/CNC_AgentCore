// Domain/Abstractions/IRerankClient.cs —— 自实现 Rerank 接口（生态无标准）
namespace CNC_AgentCore.Domain.Abstractions;

public interface IRerankClient
{
    Task<IReadOnlyList<(int OriginalIndex, double Score)>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int? topN = null,
        CancellationToken ct = default);
}
