// Api/Endpoints/DeviceEndpoints.cs —— /api/devices 设备台账主数据 CRUD（组级 RequireAuth）
// 契约 DTO 在 Api/Contracts；本文件只做参数校验/抛 ApiException。
using System.Text;
using System.Text.Json;
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class DeviceEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> ValidStatus = new(StringComparer.Ordinal)
    {
        "running", "idle", "repair", "scrapped",
    };

    private const string StatusMsg = "status 必须为 running/idle/repair/scrapped 之一";

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/devices").WithTags("devices")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapGet("", List).WithName("DeviceList");
        g.MapPost("", Create).WithName("DeviceCreate");
        g.MapPut("/{id:long}", Update).WithName("DeviceUpdate");
        g.MapDelete("/{id:long}", Delete).WithName("DeviceDelete");
        return app;
    }

    // ===== GET /api/devices =====

    private static async Task<IResult> List(IDeviceRepository devices, HttpContext http)
    {
        var status = http.Request.Query["status"].ToString();
        if (status.Length > 0 && !ValidStatus.Contains(status))
            throw new ApiException(422, "invalid_status", StatusMsg);

        var brand = http.Request.Query["brand"].ToString();
        var q = http.Request.Query["q"].ToString();
        if (q.Length > 100)
            throw new ApiException(422, "invalid_q", "q 长度不能超过 100");

        var limit = int.TryParse(http.Request.Query["limit"].ToString(), out var l) ? Math.Clamp(l, 1, 200) : 20;
        var offset = int.TryParse(http.Request.Query["offset"].ToString(), out var o) ? Math.Max(o, 0) : 0;

        var query = new DeviceListQuery(
            Status: status.Length == 0 ? null : status,
            Brand: brand.Length == 0 ? null : brand,
            Q: q.Length == 0 ? null : q,
            Limit: limit,
            Offset: offset);
        var (items, total) = await devices.ListAsync(query, http.RequestAborted);

        return Results.Ok(new DeviceListResponseDto
        {
            Total = total,
            Items = items.Select(ToItemDto).ToList(),
            Limit = limit,
            Offset = offset,
        });
    }

    // ===== POST /api/devices =====

    private static async Task<IResult> Create(IDeviceRepository devices, HttpContext http)
    {
        var body = await ReadBodyAsync<CreateDeviceRequestDto>(http);

        if (string.IsNullOrWhiteSpace(body.AssetNo) || body.AssetNo.Length > 64)
            throw new ApiException(422, "invalid_asset_no", "asset_no 必填且长度不超过 64");
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 128)
            throw new ApiException(422, "invalid_name", "name 必填且长度不超过 128");
        if (string.IsNullOrWhiteSpace(body.Brand) || body.Brand.Length > 64)
            throw new ApiException(422, "invalid_brand", "brand 必填且长度不超过 64");
        if (!ValidStatus.Contains(body.Status))
            throw new ApiException(422, "invalid_status", StatusMsg);
        CheckLen(body.Model, "model", 64);
        CheckLen(body.Controller, "controller", 64);
        CheckLen(body.Workshop, "workshop", 64);
        CheckLen(body.LineNo, "line_no", 64);

        long id;
        try
        {
            id = await devices.CreateAsync(new CreateDeviceRequest(
                AssetNo: body.AssetNo,
                Name: body.Name,
                Brand: body.Brand,
                Model: body.Model,
                Controller: body.Controller,
                Workshop: body.Workshop,
                LineNo: body.LineNo,
                InstallDate: body.InstallDate,
                Status: body.Status,
                IsDemo: body.IsDemo,
                Spec: body.Spec), http.RequestAborted);
        }
        catch (DeviceConflictException ex)
        {
            throw new ApiException(409, "conflict", ex.Message);
        }

        return Results.Ok(new CreateDeviceResponseDto { Id = id, AssetNo = body.AssetNo });
    }

    // ===== PUT /api/devices/{id} =====

    private static async Task<IResult> Update(long id, IDeviceRepository devices, HttpContext http)
    {
        var body = await ReadBodyAsync<UpdateDeviceRequestDto>(http);

        if (body.Status is not null && !ValidStatus.Contains(body.Status))
            throw new ApiException(422, "invalid_status", StatusMsg);
        CheckOptionalNonEmpty(body.Name, "name", 128);
        CheckOptionalNonEmpty(body.Brand, "brand", 64);
        CheckLen(body.Model, "model", 64);
        CheckLen(body.Controller, "controller", 64);
        CheckLen(body.Workshop, "workshop", 64);
        CheckLen(body.LineNo, "line_no", 64);

        var ok = await devices.UpdateAsync(id, new UpdateDeviceRequest(
            Name: body.Name,
            Brand: body.Brand,
            Model: body.Model,
            Controller: body.Controller,
            Workshop: body.Workshop,
            LineNo: body.LineNo,
            InstallDate: body.InstallDate,
            Status: body.Status,
            IsDemo: body.IsDemo,
            Spec: body.Spec), http.RequestAborted);
        if (!ok)
            throw new ApiException(404, "not_found", $"device id={id} 不存在");

        return Results.Ok(new UpdateDeviceResponseDto { Id = id, Updated = true });
    }

    // ===== DELETE /api/devices/{id} =====

    private static async Task<IResult> Delete(long id, IDeviceRepository devices, HttpContext http)
    {
        var woCount = await devices.CountWorkordersAsync(id, http.RequestAborted);
        if (woCount > 0)
            throw new ApiException(409, "device_in_use",
                $"该设备关联 {woCount} 条维修工单，请先删除/转移工单后再删设备");

        var ok = await devices.DeleteAsync(id, http.RequestAborted);
        if (!ok)
            throw new ApiException(404, "not_found", $"device id={id} 不存在");

        return Results.Ok(new DeleteDeviceResponseDto { Deleted = id });
    }

    // ===== helpers =====

    private static DeviceItemDto ToItemDto(DeviceItem d) => new()
    {
        Id = d.Id,
        AssetNo = d.AssetNo,
        Name = d.Name,
        Brand = d.Brand,
        Model = d.Model,
        Controller = d.Controller,
        Workshop = d.Workshop,
        LineNo = d.LineNo,
        InstallDate = d.InstallDate,
        Status = d.Status,
        IsDemo = d.IsDemo,
        Spec = d.Spec,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

    private static void CheckLen(string? value, string field, int max)
    {
        if (value is not null && value.Length > max)
            throw new ApiException(422, $"invalid_{field}", $"{field} 长度不能超过 {max}");
    }

    // 空串也 422（null/缺省则忽略）
    private static void CheckOptionalNonEmpty(string? value, string field, int max)
    {
        if (value is not null && (value.Length == 0 || value.Length > max))
            throw new ApiException(422, $"invalid_{field}", $"{field} 不能为空且长度不超过 {max}");
    }

    /// <summary>读取并反序列化 JSON body；失败一律 422。</summary>
    private static async Task<T> ReadBodyAsync<T>(HttpContext http) where T : class
    {
        string raw;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
            raw = await reader.ReadToEndAsync(http.RequestAborted);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ApiException(422, "invalid_body", "请求体不能为空");
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(raw, JsonOpts);
            if (parsed is null)
                throw new ApiException(422, "invalid_body", "请求体反序列化失败");
            return parsed;
        }
        catch (JsonException)
        {
            throw new ApiException(422, "invalid_body", "请求体 JSON 格式无效");
        }
    }
}
