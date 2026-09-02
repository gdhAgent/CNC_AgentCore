// Domain/Entities/RolePermission.cs —— ops.role_permissions
namespace CNC_AgentCore.Domain.Entities;

public sealed class RolePermission
{
    public long Id { get; set; }
    public string Role { get; set; } = string.Empty;           // admin/operator/viewer
    public string PageCode { get; set; } = string.Empty;
    public bool CanAccess { get; set; } = true;
    public string[] Actions { get; set; } = Array.Empty<string>();
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
