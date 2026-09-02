// Domain/Abstractions/IDeviceRepository.cs —— ops.machines 设备台账仓储
// 列表/请求模型集中于此供接口签名用；契约 DTO 在 Api/Contracts（[JsonPropertyName] snake_case）。
namespace CNC_AgentCore.Domain.Abstractions;

/// <summary>GET /api/devices 列表单条。</summary>
public sealed record DeviceItem(
    long Id,
    string AssetNo,
    string Name,
    string Brand,
    string? Model,
    string? Controller,
    string? Workshop,
    string? LineNo,
    DateOnly? InstallDate,
    string Status,
    bool IsDemo,
    Dictionary<string, object?> Spec,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>POST /api/devices 请求（由 CreateDeviceRequestDto 映射而来）。</summary>
public sealed record CreateDeviceRequest(
    string AssetNo,
    string Name,
    string Brand,
    string? Model,
    string? Controller,
    string? Workshop,
    string? LineNo,
    DateOnly? InstallDate,
    string Status,
    bool IsDemo,
    Dictionary<string, object?>? Spec);

/// <summary>PUT /api/devices/{id} 请求：全字段可空，入库走 COALESCE（缺省/null = 保持原值）。asset_no 不可改。</summary>
public sealed record UpdateDeviceRequest(
    string? Name,
    string? Brand,
    string? Model,
    string? Controller,
    string? Workshop,
    string? LineNo,
    DateOnly? InstallDate,
    string? Status,
    bool? IsDemo,
    Dictionary<string, object?>? Spec);

/// <summary>GET /api/devices 查询条件。</summary>
public sealed record DeviceListQuery(
    string? Status = null,
    string? Brand = null,
    string? Q = null,
    int Limit = 20,
    int Offset = 0);

public interface IDeviceRepository
{
    Task<(List<DeviceItem> Items, int Total)> ListAsync(DeviceListQuery query, CancellationToken ct = default);

    /// <summary>新增设备；asset_no 唯一冲突抛 <see cref="DeviceConflictException"/>（API 层映射 409）。</summary>
    Task<long> CreateAsync(CreateDeviceRequest req, CancellationToken ct = default);

    /// <summary>更新设备（asset_no 不可改）；不存在返回 false。</summary>
    Task<bool> UpdateAsync(long id, UpdateDeviceRequest req, CancellationToken ct = default);

    /// <summary>该设备关联的维修工单数（DELETE 前置检查：&gt;0 应 409）。</summary>
    Task<int> CountWorkordersAsync(long id, CancellationToken ct = default);

    /// <summary>删除设备；不存在返回 false。</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>ops.machines.asset_no 唯一冲突。</summary>
public sealed class DeviceConflictException : Exception
{
    public DeviceConflictException(string message) : base(message) { }
}
