// Api/Endpoints/UserEndpoints.cs —— /api/users 用户管理 CRUD + 重置密码（整组 RequireRole("admin")）
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").WithTags("users")
            .AddEndpointFilter(AuthFilters.RequireRole("admin"));
        g.MapGet("", ListUsers).WithName("ListUsers");
        g.MapGet("/{id:long}", GetUser).WithName("GetUser");
        g.MapPost("", CreateUser).WithName("CreateUser");
        g.MapPut("/{id:long}", UpdateUser).WithName("UpdateUser");
        g.MapDelete("/{id:long}", DeleteUser).WithName("DeleteUser");
        g.MapPost("/{id:long}/password", ResetPassword).WithName("ResetPassword");
        return app;
    }

    private static async Task<IResult> ListUsers(IUserRepository users, HttpContext http)
    {
        var q = http.Request.Query["q"].ToString();
        var role = http.Request.Query["role"].ToString();
        var isActive = bool.TryParse(http.Request.Query["is_active"].ToString(), out var ia) ? ia : (bool?)null;
        var limit = int.TryParse(http.Request.Query["limit"].ToString(), out var l) ? Math.Clamp(l, 1, 200) : 50;
        var offset = int.TryParse(http.Request.Query["offset"].ToString(), out var o) ? Math.Max(o, 0) : 0;

        var (items, total) = await users.ListAsync(
            string.IsNullOrWhiteSpace(role) ? null : role,
            isActive,
            string.IsNullOrWhiteSpace(q) ? null : q,
            limit, offset, http.RequestAborted);

        return Results.Ok(new UserListResponse
        {
            Items = items.Select(AuthEndpoints.ToUserOut).ToList(),
            Total = total,
            Limit = limit,
            Offset = offset,
        });
    }

    private static async Task<IResult> GetUser(long id, IUserRepository users, HttpContext http)
    {
        var user = await users.GetByIdAsync(id, http.RequestAborted)
            ?? throw new ApiException(404, "not_found", $"user id={id} 不存在");
        return Results.Ok(AuthEndpoints.ToUserOut(user));
    }

    private static async Task<IResult> CreateUser(CreateUserRequest req, IUserRepository users, IPasswordHasher hasher, HttpContext http)
    {
        var payload = AuthContext.Current(http)!;
        var role = ValidateRole(req.Role);
        var hash = hasher.EncodeHash(req.Password);

        long id;
        try
        {
            id = await users.CreateAsync(req.Username, req.DisplayName, hash, role, req.IsActive, payload.Username, http.RequestAborted);
        }
        catch (UserExistsException ex)
        {
            throw new ApiException(409, "conflict", ex.Message);
        }

        var user = await users.GetByIdAsync(id, http.RequestAborted) ?? throw new ApiException(500, "internal_error", "创建后查询失败");
        return Results.Json(AuthEndpoints.ToUserOut(user), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateUser(long id, UpdateUserRequest req, IUserRepository users, HttpContext http)
    {
        if (await users.GetByIdAsync(id, http.RequestAborted) is null)
            throw new ApiException(404, "not_found", $"user id={id} 不存在");
        if (req.DisplayName is null && req.Role is null && req.IsActive is null)
            throw new ApiException(422, "empty_update", "display_name / role / is_active 至少一个");

        var role = req.Role is null ? null : ValidateRole(req.Role);
        await users.UpdateAsync(id, req.DisplayName, role, req.IsActive, http.RequestAborted);

        var updated = await users.GetByIdAsync(id, http.RequestAborted) ?? throw new ApiException(500, "internal_error", "更新后查询失败");
        return Results.Ok(AuthEndpoints.ToUserOut(updated));
    }

    private static async Task<IResult> DeleteUser(long id, IUserRepository users, HttpContext http)
    {
        var payload = AuthContext.Current(http)!;
        if (id == payload.Uid)
            throw new ApiException(422, "self_delete", "不能删除自己");
        if (await users.GetByIdAsync(id, http.RequestAborted) is null)
            throw new ApiException(404, "not_found", $"user id={id} 不存在");
        await users.DeleteAsync(id, http.RequestAborted);
        return Results.Ok(new { deleted = id });
    }

    private static async Task<IResult> ResetPassword(long id, ResetPasswordRequest req, IUserRepository users, IPasswordHasher hasher, HttpContext http)
    {
        if (await users.GetByIdAsync(id, http.RequestAborted) is null)
            throw new ApiException(404, "not_found", $"user id={id} 不存在");
        var newHash = hasher.EncodeHash(req.NewPassword);
        await users.UpdatePasswordAsync(id, newHash, http.RequestAborted);
        return Results.Ok(new { ok = true });
    }

    private static string ValidateRole(string role)
        => role is "admin" or "operator" or "viewer"
            ? role
            : throw new ApiException(422, "invalid_role", "role 必须为 admin/operator/viewer");
}
