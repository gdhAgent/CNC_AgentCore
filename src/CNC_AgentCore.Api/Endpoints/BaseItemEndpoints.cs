// Api/Endpoints/BaseItemEndpoints.cs —— /api/base-items 基础数据（枚举字典）CRUD（组级 RequireAuth）
// kind 合法值集合在 Domain/Abstractions（BaseItemKinds.Valid）；当数据存，非 C# enum。
using System.Text;
using System.Text.Json;
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class BaseItemEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string KindMsg =
        $"kind 必须为 {string.Join("/", BaseItemKinds.Valid)} 之一";

    public static IEndpointRouteBuilder MapBaseItemEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/base-items").WithTags("base-items")
            .AddEndpointFilter(AuthFilters.RequireAuth());
        g.MapGet("", List).WithName("BaseItemList");
        g.MapPost("", Create).WithName("BaseItemCreate");
        g.MapPut("/{id:long}", Update).WithName("BaseItemUpdate");
        g.MapDelete("/{id:long}", Delete).WithName("BaseItemDelete");
        return app;
    }

    // ===== GET /api/base-items =====

    private static async Task<IResult> List(IBaseItemRepository repo, HttpContext http)
    {
        var kind = http.Request.Query["kind"].ToString();
        if (kind.Length > 0 && !BaseItemKinds.Valid.Contains(kind))
            throw new ApiException(400, "invalid_kind", KindMsg);

        var includeInactive = bool.TryParse(http.Request.Query["include_inactive"].ToString(), out var b) && b;

        var (items, total) = await repo.ListAsync(
            kind.Length == 0 ? null : kind, includeInactive, http.RequestAborted);

        return Results.Ok(new BaseItemListResponseDto
        {
            Total = total,
            Items = items.Select(ToItemDto).ToList(),
        });
    }

    // ===== POST /api/base-items =====

    private static async Task<IResult> Create(IBaseItemRepository repo, HttpContext http)
    {
        var body = await ReadBodyAsync<CreateBaseItemRequestDto>(http);

        if (!BaseItemKinds.Valid.Contains(body.Kind))
            throw new ApiException(400, "invalid_kind", KindMsg);
        if (string.IsNullOrWhiteSpace(body.Code) || body.Code.Length > 64)
            throw new ApiException(422, "invalid_code", "code 必填且长度不超过 64");
        if (string.IsNullOrWhiteSpace(body.LabelZh) || body.LabelZh.Length > 128)
            throw new ApiException(422, "invalid_label_zh", "label_zh 必填且长度不超过 128");
        if (string.IsNullOrWhiteSpace(body.LabelEn) || body.LabelEn.Length > 128)
            throw new ApiException(422, "invalid_label_en", "label_en 必填且长度不超过 128");
        if (body.SortOrder < 0 || body.SortOrder > 10000)
            throw new ApiException(422, "invalid_sort_order", "sort_order 取值范围 0..10000");

        long id;
        try
        {
            id = await repo.CreateAsync(new CreateBaseItemRequest(
                Kind: body.Kind,
                Code: body.Code,
                LabelZh: body.LabelZh,
                LabelEn: body.LabelEn,
                SortOrder: body.SortOrder,
                IsActive: body.IsActive), http.RequestAborted);
        }
        catch (BaseItemConflictException ex)
        {
            throw new ApiException(409, "conflict", ex.Message);
        }

        return Results.Ok(new CreateBaseItemResponseDto { Id = id, Kind = body.Kind, Code = body.Code });
    }

    // ===== PUT /api/base-items/{id} =====

    private static async Task<IResult> Update(long id, IBaseItemRepository repo, HttpContext http)
    {
        var body = await ReadBodyAsync<UpdateBaseItemRequestDto>(http);

        // kind/code 不可改：请求体里出现也忽略
        CheckOptionalNonEmpty(body.LabelZh, "label_zh", 128);
        CheckOptionalNonEmpty(body.LabelEn, "label_en", 128);
        if (body.SortOrder is not null && (body.SortOrder < 0 || body.SortOrder > 10000))
            throw new ApiException(422, "invalid_sort_order", "sort_order 取值范围 0..10000");

        var ok = await repo.UpdateAsync(id, new UpdateBaseItemRequest(
            LabelZh: body.LabelZh,
            LabelEn: body.LabelEn,
            SortOrder: body.SortOrder,
            IsActive: body.IsActive), http.RequestAborted);
        if (!ok)
            throw new ApiException(404, "not_found", $"base_item id={id} 不存在");

        return Results.Ok(new UpdateBaseItemResponseDto { Id = id, Updated = true });
    }

    // ===== DELETE /api/base-items/{id} =====

    private static async Task<IResult> Delete(long id, IBaseItemRepository repo, HttpContext http)
    {
        var ok = await repo.DeleteAsync(id, http.RequestAborted);
        if (!ok)
            throw new ApiException(404, "not_found", $"base_item id={id} 不存在");

        return Results.Ok(new DeleteBaseItemResponseDto { Deleted = id });
    }

    // ===== helpers =====

    private static BaseItemDto ToItemDto(BaseItem b) => new()
    {
        Id = b.Id,
        Kind = b.Kind,
        Code = b.Code,
        LabelZh = b.LabelZh,
        LabelEn = b.LabelEn,
        SortOrder = b.SortOrder,
        IsActive = b.IsActive,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt,
    };

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
