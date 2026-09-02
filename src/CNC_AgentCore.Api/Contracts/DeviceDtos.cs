// Api/Contracts/DeviceDtos.cs —— /api/devices 端点契约
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class CreateDeviceRequestDto
{
    [JsonPropertyName("asset_no")] public string AssetNo { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("brand")] public string Brand { get; set; } = "";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("workshop")] public string? Workshop { get; set; }
    [JsonPropertyName("line_no")] public string? LineNo { get; set; }
    [JsonPropertyName("install_date")] public DateOnly? InstallDate { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "running";
    [JsonPropertyName("is_demo")] public bool IsDemo { get; set; } = true;
    [JsonPropertyName("spec")] public Dictionary<string, object?>? Spec { get; set; }
}

public sealed class UpdateDeviceRequestDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("workshop")] public string? Workshop { get; set; }
    [JsonPropertyName("line_no")] public string? LineNo { get; set; }
    [JsonPropertyName("install_date")] public DateOnly? InstallDate { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("is_demo")] public bool? IsDemo { get; set; }
    [JsonPropertyName("spec")] public Dictionary<string, object?>? Spec { get; set; }
}

public sealed class DeviceItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("asset_no")] public string AssetNo { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("brand")] public string Brand { get; set; } = "";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("controller")] public string? Controller { get; set; }
    [JsonPropertyName("workshop")] public string? Workshop { get; set; }
    [JsonPropertyName("line_no")] public string? LineNo { get; set; }
    [JsonPropertyName("install_date")] public DateOnly? InstallDate { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("is_demo")] public bool IsDemo { get; set; }
    [JsonPropertyName("spec")] public Dictionary<string, object?> Spec { get; set; } = new();
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DeviceListResponseDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("items")] public List<DeviceItemDto> Items { get; set; } = new();
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

public sealed class CreateDeviceResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("asset_no")] public string AssetNo { get; set; } = "";
}

public sealed class UpdateDeviceResponseDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("updated")] public bool Updated { get; set; }
}

public sealed class DeleteDeviceResponseDto
{
    [JsonPropertyName("deleted")] public long Deleted { get; set; }
}
