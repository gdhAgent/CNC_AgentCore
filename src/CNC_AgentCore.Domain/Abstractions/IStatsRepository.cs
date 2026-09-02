// Domain/Abstractions/IStatsRepository.cs —— 高频故障 Top-N 数据访问
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>单条聚合项（query / maintenance 通用）。name/severity/brand 仅维护侧有；query 侧由 EnrichCodesAsync 补齐。</summary>
public sealed record TopFaultAggItem(
    string CodeNorm,
    int Count,
    DateTimeOffset? LastSeenAt,
    string? Name = null,
    string? Severity = null,
    string? Brand = null);

/// <summary>kb.alarms 补全信息（query 侧 enrich 用）。</summary>
public sealed record CodeEnrichMeta(string Name, string? Severity, string? Brand);

public interface IStatsRepository
{
    /// <summary>查询侧：log.query_logs.detected_codes 拆 array 聚合。返回 (items, total_query_logs)。</summary>
    Task<(List<TopFaultAggItem> Items, long Total)> FetchTopByQueryAsync(
        DateTimeOffset? fromTime, DateTimeOffset? toTime, int topN, CancellationToken ct = default);

    /// <summary>工单侧：ops.maintenance_logs.alarm_code 聚合 + LEFT JOIN machines + LEFT JOIN kb.alarms。返回 (items, total_maintenance_logs)。</summary>
    Task<(List<TopFaultAggItem> Items, long Total)> FetchTopByMaintenanceAsync(
        DateTimeOffset? fromTime, DateTimeOffset? toTime, int topN, CancellationToken ct = default);

    /// <summary>批量补全 code_norm → {name, severity, brand}（kb.alarms 不存在则不入 dict）。</summary>
    Task<IReadOnlyDictionary<string, CodeEnrichMeta>> EnrichCodesAsync(
        IReadOnlyCollection<string> codeNorms, CancellationToken ct = default);
}
