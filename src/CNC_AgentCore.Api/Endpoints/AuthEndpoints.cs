// Api/Endpoints/AuthEndpoints.cs —— /api/auth 登录 / 当前用户 / 登出 / 改密
// /me、/logout、/change-password 需 RequireAuth。JWT 无状态；logout 仅审计打点。
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Errors;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Application.Auth;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Entities;
using CNC_AgentCore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("auth");
        g.MapPost("/login", Login).WithName("Login");
        g.MapGet("/me", Me).AddEndpointFilter(AuthFilters.RequireAuth()).WithName("Me");
        g.MapPost("/logout", Logout).AddEndpointFilter(AuthFilters.RequireAuth()).WithName("Logout");
        g.MapPost("/change-password", ChangePassword).AddEndpointFilter(AuthFilters.RequireAuth()).WithName("ChangePassword");
        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest req, IUserRepository users, IPasswordHasher hasher,
        IJwtService jwt, JwtOptions jwtOpts, HttpContext http)
    {
        var user = await users.GetByUsernameAsync(req.Username, http.RequestAborted);
        var ok = user is not null && user.IsActive && hasher.VerifyPassword(req.Password, user.PasswordHash);
        if (!ok)
            throw new ApiException(401, "auth_failed", "用户名或密码错误");   // 不区分用户不存在/密码错/停用

        var token = jwt.IssueToken(user!.Id, user.Username, RoleStr(user.Role), user.DisplayName);
        await users.TouchLastLoginAsync(user.Id, http.RequestAborted);

        // needs_rehash → 透明升级迭代次数
        if (hasher.NeedsRehash(user.PasswordHash))
        {
            var newHash = hasher.EncodeHash(req.Password);
            await users.UpdatePasswordAsync(user.Id, newHash, http.RequestAborted);
        }

        return Results.Ok(new LoginResponse { Token = token, ExpiresIn = jwtOpts.TtlSec, User = ToUserOut(user) });
    }

    private static async Task<IResult> Me(IUserRepository users, IRolePermissionRepository perms, HttpContext http)
    {
        var payload = AuthContext.Current(http) ?? throw new ApiException(401, "auth_required", "请先登录");
        var user = await users.GetByIdAsync(payload.Uid, http.RequestAborted)
            ?? throw new ApiException(401, "user_not_found", "用户不存在");

        var visible = await perms.GetRoleVisiblePagesAsync(payload.Role, http.RequestAborted);
        var actionsMap = await perms.GetRolePermissionsMapAsync(payload.Role, http.RequestAborted);

        return Results.Ok(new MeResponse
        {
            User = ToUserOut(user),
            VisiblePages = visible.OrderBy(x => x).ToList(),
            ActionsByPage = actionsMap.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(x => x).ToList()),
        });
    }

    private static IResult Logout(ILoggerFactory loggerFactory, HttpContext http)
    {
        var payload = AuthContext.Current(http);
        if (payload is not null)
        {
            var log = loggerFactory.CreateLogger("AuthEndpoints");
            log.LogInformation("user logged out uid={Uid} username={Username}", payload.Uid, payload.Username);
        }
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest req, IUserRepository users, IPasswordHasher hasher, HttpContext http)
    {
        var payload = AuthContext.Current(http) ?? throw new ApiException(401, "auth_required", "请先登录");
        var user = await users.GetByIdAsync(payload.Uid, http.RequestAborted)
            ?? throw new ApiException(401, "user_not_found", "用户不存在");

        if (!hasher.VerifyPassword(req.OldPassword, user.PasswordHash))
            throw new ApiException(401, "auth_failed", "旧密码错误");

        var newHash = hasher.EncodeHash(req.NewPassword);
        await users.UpdatePasswordAsync(user.Id, newHash, http.RequestAborted);
        return Results.Ok(new { ok = true });
    }

    internal static UserOut ToUserOut(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        DisplayName = u.DisplayName,
        Role = RoleStr(u.Role),
        IsActive = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        CreatedAt = u.CreatedAt,
        UpdatedAt = u.UpdatedAt,
        CreatedBy = u.CreatedBy,
    };

    internal static string RoleStr(Role role) => role.ToString().ToLowerInvariant();
}
