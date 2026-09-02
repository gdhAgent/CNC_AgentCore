// Application/Common/ApiError.cs —— 统一错误响应。
namespace CNC_AgentCore.Application.Common;

public sealed class ApiError
{
    public int StatusCode { get; }
    public string Code { get; }
    public string Message { get; }
    public Dictionary<string, object?>? Detail { get; }

    public ApiError(int statusCode, string code, string message, Dictionary<string, object?>? detail = null)
    {
        StatusCode = statusCode;
        Code = code;
        Message = message;
        Detail = detail;
    }

    public override string ToString() => $"[{StatusCode}] {Code}: {Message}";

    // 工厂方法
    public static ApiError BadRequest(string code, string msg) => new(400, code, msg);
    public static ApiError Unauthorized(string code, string msg) => new(401, code, msg);
    public static ApiError Forbidden(string code, string msg) => new(403, code, msg);
    public static ApiError NotFound(string code, string msg) => new(404, code, msg);
    public static ApiError Unprocessable(string code, string msg) => new(422, code, msg);
    public static ApiError Internal(string code, string msg) => new(500, code, msg);
}
