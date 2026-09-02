// Api/Filters/AuthFilters.cs —— RequireAuth / RequireRole / RequireAction 三档 IEndpointFilter
// 校验 Bearer JWT，通过后把 TokenPayload 写入 http.Items[PayloadKey]，用 AuthContext.Current(http) 读取。
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;

namespace CNC_AgentCore.Api.Filters;

public static class AuthContext
{
    public const string PayloadKey = "cnc.token_payload";

    public static TokenPayload? Current(HttpContext http)
        => http.Items.TryGetValue(PayloadKey, out var v) ? v as TokenPayload : null;

    public static string ExtractBearer(HttpContext http)
    {
        var auth = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth)) return string.Empty;
        const string prefix = "Bearer ";
        return auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? auth[prefix.Length..].Trim()
            : string.Empty;
    }
}

public sealed class RequireAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var jwt = ctx.HttpContext.RequestServices.GetRequiredService<IJwtService>();
        var token = AuthContext.ExtractBearer(ctx.HttpContext);
        var payload = token.Length == 0 ? null : jwt.SafeDecode(token);
        if (payload is null)
            return Results.Json(ApiErrorResponse.Of("auth_required", "请先登录"),
                statusCode: StatusCodes.Status401Unauthorized);
        ctx.HttpContext.Items[AuthContext.PayloadKey] = payload;
        return await next(ctx);
    }
}

public sealed class RequireRoleFilter : IEndpointFilter
{
    private readonly string[] _roles;

    public RequireRoleFilter(params string[] roles) => _roles = roles;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var jwt = ctx.HttpContext.RequestServices.GetRequiredService<IJwtService>();
        var token = AuthContext.ExtractBearer(ctx.HttpContext);
        var payload = token.Length == 0 ? null : jwt.SafeDecode(token);
        if (payload is null)
            return Results.Json(ApiErrorResponse.Of("auth_required", "请先登录"),
                statusCode: StatusCodes.Status401Unauthorized);
        if (!_roles.Contains(payload.Role))
            return Results.Json(ApiErrorResponse.Of("forbidden", "无权限执行此操作"),
                statusCode: StatusCodes.Status403Forbidden);
        ctx.HttpContext.Items[AuthContext.PayloadKey] = payload;
        return await next(ctx);
    }
}

public sealed class RequireActionFilter : IEndpointFilter
{
    private readonly string _pageCode;
    private readonly string _action;

    public RequireActionFilter(string pageCode, string action)
    {
        _pageCode = pageCode;
        _action = action;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var jwt = ctx.HttpContext.RequestServices.GetRequiredService<IJwtService>();
        var token = AuthContext.ExtractBearer(ctx.HttpContext);
        var payload = token.Length == 0 ? null : jwt.SafeDecode(token);
        if (payload is null)
            return Results.Json(ApiErrorResponse.Of("auth_required", "请先登录"),
                statusCode: StatusCodes.Status401Unauthorized);

        var perms = ctx.HttpContext.RequestServices.GetRequiredService<IRolePermissionRepository>();
        var map = await perms.GetRolePermissionsMapAsync(payload.Role, ctx.HttpContext.RequestAborted);
        var allowed = map.TryGetValue(_pageCode, out var acts) && acts.Contains(_action);
        if (!allowed)
            return Results.Json(ApiErrorResponse.Of("forbidden", $"缺少权限 {_pageCode}.{_action}"),
                statusCode: StatusCodes.Status403Forbidden);

        ctx.HttpContext.Items[AuthContext.PayloadKey] = payload;
        return await next(ctx);
    }
}

public static class AuthFilters
{
    public static IEndpointFilter RequireAuth() => new RequireAuthFilter();
    public static IEndpointFilter RequireRole(params string[] roles) => new RequireRoleFilter(roles);
    public static IEndpointFilter RequireAction(string pageCode, string action) => new RequireActionFilter(pageCode, action);
}
