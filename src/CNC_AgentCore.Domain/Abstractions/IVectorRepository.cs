// Domain/Abstractions/IVectorRepository.cs —— 向量总览数据访问（#10 / #11）。
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>单张向量表的覆盖统计（#10）。
/// DimMin/DimMax 为 NULL 时表示该表无任何已向量化记录（vector_dims 不可用于 NULL）。</summary>
public sealed record VectorTableStat(
    string Table,
    string Label,
    string Note,
    bool DesignedSkip,
    long Total,
    long WithEmbedding,
    long Without,
    int? DimMin,
    int? DimMax);

/// <summary>无向量清单单条（#11）。三表字段不完全对齐：
/// - alarms：Code 报警码；Detail=null
/// - chunks：Level=2；Code=null；Detail 为 content 截断 80 字
/// - maintenance_logs：Code=order_no 或"无工单号"；Detail=null</summary>
public sealed record UnvectorizedItem(
    long Id,
    string? Code,
    int? Level,
    string Title,
    string? Detail);

public interface IVectorRepository
{
    /// <summary>三张向量表（kb.alarms / kb.chunks / ops.maintenance_logs）的覆盖统计。
    /// chunks 仅统计 level=2 子块（父块按设计不向量化，不计入"缺"的口径）。
    /// 实现内部并行三表 fetch。</summary>
    Task<List<VectorTableStat>> GetOverviewAsync(CancellationToken ct = default);

    /// <summary>无向量清单（#11）。table ∈ {alarms, chunks, maintenance_logs}；chunks 只列 level=2 子块。
    /// 返回 (items, total)。</summary>
    Task<(List<UnvectorizedItem> Items, int Total)> ListUnvectorizedAsync(
        string table, int limit, int offset, CancellationToken ct = default);

    /// <summary>已向量化记录的 raw embedding（#13，端点经 VectorPca 做服务端 PCA）。table ∈ {alarms,chunks,maintenance_logs}；
    /// groupBy 须在 VectorRepository.GroupByOptions[table] 内；limit 默认 200、上限 200。</summary>
    Task<List<EmbeddingMapItem>> FetchEmbeddingMapAsync(
        string table, string groupBy, int limit, CancellationToken ct = default);
}

/// <summary>embedding-map 单条（#13）：id + label + group + 服务端 PCA 2D 坐标。</summary>
public sealed record EmbeddingMapItem(
    long Id,
    string Label,
    string Group,
    float[] Vec);
