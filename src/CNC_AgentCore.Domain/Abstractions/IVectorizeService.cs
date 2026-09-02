// Domain/Abstractions/IVectorizeService.cs —— 后台补跑向量服务（#12）。
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>单次向量化的结果汇总（#12）。</summary>
public sealed record VectorizeResult(
    string Table,
    int TotalCandidates,
    int Embedded,
    int Failed,
    int SkippedEmptyText,
    int ElapsedMs);

public interface IVectorizeService
{
    /// <summary>拉空 embedding IS NULL 行 → 拼文本 → 批量向量化 → UPDATE 写回。
    /// 失败批次不影响其他批次；返回汇总。</summary>
    Task<VectorizeResult> RunAsync(string table, int batch, CancellationToken ct = default);
}
