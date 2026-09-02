// Domain/Entities/BaseItem.cs —— ops.base_items（基础数据字典）
namespace CNC_AgentCore.Domain.Entities;

public sealed class BaseItem
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;  // fault_type/engineer/...
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
