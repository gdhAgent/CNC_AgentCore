// Api/Middleware/ApiErrorExceptionMiddleware.cs —— 统一异常 → {"error": {code, message}}
// ApiException 按自身 status/code；其余异常兜底 500 internal_error。
using System.Text.Json;
using CNC_AgentCore.Api.Errors;

namespace CNC_AgentCore.Api.Middleware;

public sealed class ApiErrorExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiErrorExceptionMiddleware> _log;

    public ApiErrorExceptionMiddleware(RequestDelegate next, ILogger<ApiErrorExceptionMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await _next(http);
        }
        catch (ApiException ex)
        {
            _log.LogWarning("ApiError {Code}: {Message}", ex.Code, ex.Message);
            await WriteErrorAsync(http, ex.StatusCode, ApiErrorResponse.Of(ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "未处理异常 {Path}", http.Request.Path);
            await WriteErrorAsync(http, StatusCodes.Status500InternalServerError,
                ApiErrorResponse.Of("internal_error", "服务器内部错误"));
        }
    }

    private static async Task WriteErrorAsync(HttpContext http, int statusCode, ApiErrorResponse body)
    {
        if (http.Response.HasStarted) return;
        http.Response.Clear();
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
