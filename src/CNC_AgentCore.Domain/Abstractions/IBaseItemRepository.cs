// Domain/Abstractions/IBaseItemRepository.cs —— kb.base_items 枚举字典仓储
// kind 校验集合硬编码（数据/校验集，非 enum）。
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>kb.base_items.kind 合法值。</summary>
public static class BaseItemKinds
{
    public static readonly string[] Valid =
    {
        "brand", "category", "severity", "fault_type",
    };
}

/// <summary>GET /api/base-items 列表单条。</summary>
public sealed record BaseItem(
    long Id,
    string Kind,
    string Code,
    string LabelZh,
    string LabelEn,
    int SortOrder,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>POST /api/base-items 请求。</summary>
public sealed record CreateBaseItemRequest(
    string Kind,
    string Code,
    string LabelZh,
    string LabelEn,
    int SortOrder,
    bool IsActive);

/// <summary>PUT /api/base-items/{id} 请求：kind/code 不可改，其余可空走 COALESCE。</summary>
public sealed record UpdateBaseItemRequest(
    string? LabelZh,
    string? LabelEn,
    int? SortOrder,
    bool? IsActive);

public interface IBaseItemRepository
{
    Task<(List<BaseItem> Items, int Total)> ListAsync(string? kind, bool includeInactive, CancellationToken ct = default);

    /// <summary>新增；UNIQUE(kind, code) 冲突抛 <see cref="BaseItemConflictException"/>（API 层映射 409）。</summary>
    Task<long> CreateAsync(CreateBaseItemRequest req, CancellationToken ct = default);

    /// <summary>更新 label/sort/is_active；不存在返回 false。</summary>
    Task<bool> UpdateAsync(long id, UpdateBaseItemRequest req, CancellationToken ct = default);

    /// <summary>硬删除；不存在返回 false。</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>kb.base_items UNIQUE(kind, code) 冲突。</summary>
public sealed class BaseItemConflictException : Exception
{
    public BaseItemConflictException(string message) : base(message) { }
}
