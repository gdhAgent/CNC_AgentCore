// Api/Contracts/BaseItemDtos.cs —— /api/base-items 端点契约
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class CreateBaseItemRequestDto
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("label_zh")] public string LabelZh { get; set; } = "";
    [JsonPropertyName("label_en")] public string LabelEn { get; set; } = "";
    [JsonPropertyName("sort_order")] public int SortOrder { get; set; } = 100;
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;
}

public sealed class UpdateBaseItemRequestDto
{
    [JsonPropertyName("label_zh")] public string? LabelZh { get; set; }
    [JsonPropertyName("label_en")] public string? LabelEn { get; set; }
    [JsonPropertyName("sort_order")] public int? SortOrder { get; set; }
    [JsonPropertyName("is_active")] public bool? IsActive { get; set; }
}

public sealed class BaseItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("label_zh")] public string LabelZh { get; set; } = "";
    [JsonPropertyName("label_en")] public string LabelEn { get; set; } = "";
    [JsonPropertyName("sort_order")] public int SortOrder { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BaseItemListResponseDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("items")] public List<BaseItemDto> Items { get; set; } = new();
}

public sealed class CreateBaseItemResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
}

public sealed class UpdateBaseItemResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("updated")] public bool Updated { get; set; }
}

public sealed class DeleteBaseItemResponseDto
{
    [JsonPropertyName("deleted")] public long Deleted { get; set; }
}
