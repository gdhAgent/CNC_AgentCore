// Api/Errors/ApiException.cs —— 业务异常 + 统一错误响应契约
// 响应形状：{"error": {"code", "message", "detail?"}}
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Errors;

public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public ApiException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

public sealed class ApiErrorBody
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("detail")] public object? Detail { get; set; }
}

public sealed class ApiErrorResponse
{
    [JsonPropertyName("error")] public ApiErrorBody Error { get; set; } = new();

    public static ApiErrorResponse Of(string code, string message, object? detail = null) => new()
    {
        Error = new ApiErrorBody { Code = code, Message = message, Detail = detail },
    };
}
