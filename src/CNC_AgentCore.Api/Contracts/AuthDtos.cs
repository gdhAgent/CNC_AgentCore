// Api/Contracts/AuthDtos.cs —— 登录 / 用户 CRUD / 权限矩阵
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

// ============== 登录 / 当前用户 ==============

public sealed class LoginRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
}

public sealed class UserOut
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    [JsonPropertyName("last_login_at")] public DateTimeOffset? LastLoginAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
}

public sealed class LoginResponse
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("user")] public UserOut User { get; set; } = new();
}

public sealed class MeResponse
{
    [JsonPropertyName("user")] public UserOut User { get; set; } = new();
    [JsonPropertyName("visible_pages")] public List<string> VisiblePages { get; set; } = new();
    [JsonPropertyName("actions_by_page")] public Dictionary<string, List<string>> ActionsByPage { get; set; } = new();
}

public sealed class ChangePasswordRequest
{
    [JsonPropertyName("old_password")] public string OldPassword { get; set; } = "";
    [JsonPropertyName("new_password")] public string NewPassword { get; set; } = "";
}

public sealed class ResetPasswordRequest
{
    [JsonPropertyName("new_password")] public string NewPassword { get; set; } = "";
}

// ============== 用户 CRUD ==============

public sealed class CreateUserRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "viewer";
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;
}

public sealed class UpdateUserRequest
{
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("is_active")] public bool? IsActive { get; set; }
}

public sealed class UserListResponse
{
    [JsonPropertyName("items")] public List<UserOut> Items { get; set; } = new();
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

// ============== 权限矩阵 ==============

public sealed class RolePermissionItem
{
    [JsonPropertyName("page_code")] public string PageCode { get; set; } = "";
    [JsonPropertyName("can_access")] public bool CanAccess { get; set; }
    [JsonPropertyName("actions")] public List<string> Actions { get; set; } = new();
}

public sealed class RolePermissionsResponse
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("items")] public List<RolePermissionItem> Items { get; set; } = new();
}

public sealed class RolePermissionsUpdateRequest
{
    [JsonPropertyName("items")] public List<RolePermissionItem> Items { get; set; } = new();
    [JsonPropertyName("updated_by")] public string? UpdatedBy { get; set; }
}
