// Api/Endpoints/RolePermissionEndpoints.cs —— /api/role-permissions 权限矩阵（admin）
// GET  {role} 读单角色矩阵；PUT  {role} 整角色替换（事务）。
using CNC_AgentCore.Api.Contracts;
using CNC_AgentCore.Api.Filters;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CNC_AgentCore.Api.Endpoints;

public static class RolePermissionEndpoints
{
    public static IEndpointRouteBuilder MapRolePermissionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/role-permissions").WithTags("role-permissions")
            .AddEndpointFilter(AuthFilters.RequireRole("admin"));
        // GET /api/role-permissions（无 role）→ 全角色矩阵
        g.MapGet("", GetAll).WithName("GetAllRolePermissions");
        g.MapGet("/{role}", GetRole).WithName("GetRolePermissions");
        g.MapPut("/{role}", SetRole).WithName("SetRolePermissions");
        return app;
    }

    private static async Task<IResult> GetAll(IRolePermissionRepository perms, HttpContext http)
    {
        var rows = await perms.GetAllAsync(http.RequestAborted);
        // 按 role 分组返回（前端按角色整组渲染）
        var groups = rows.GroupBy(r => r.Role).OrderBy(g => g.Key).Select(g => new
        {
            Role = g.Key,
            Items = g.OrderBy(r => r.PageCode).Select(r => new
            {
                r.PageCode,
                r.CanAccess,
                Actions = r.Actions?.ToList() ?? new List<string>(),
            }).ToList(),
        }).ToList();
        return Results.Ok(new { Roles = groups });
    }

    private static async Task<IResult> GetRole(string role, IRolePermissionRepository perms, HttpContext http)
    {
        var rows = await perms.GetAllForRoleAsync(role, http.RequestAborted);
        return Results.Ok(new RolePermissionsResponse
        {
            Role = role,
            Items = rows.Select(r => new RolePermissionItem
            {
                PageCode = r.PageCode,
                CanAccess = r.CanAccess,
                Actions = r.Actions?.ToList() ?? new(),
            }).ToList(),
        });
    }

    private static async Task<IResult> SetRole(string role, RolePermissionsUpdateRequest req, IRolePermissionRepository perms, HttpContext http)
    {
        var payload = AuthContext.Current(http);
        var updatedBy = string.IsNullOrWhiteSpace(req.UpdatedBy) ? payload?.Username : req.UpdatedBy;

        var rows = req.Items.Select(i => new RolePermission
        {
            Role = role,
            PageCode = i.PageCode,
            CanAccess = i.CanAccess,
            Actions = i.Actions.ToArray(),
        }).ToList();

        await perms.BulkSetAsync(role, rows, updatedBy ?? string.Empty, http.RequestAborted);
        return Results.Ok(new { ok = true });
    }
}
