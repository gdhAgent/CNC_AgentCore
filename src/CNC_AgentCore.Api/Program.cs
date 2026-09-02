// src/CNC_AgentCore.Api/Program.cs —— ASP.NET Core 入口：.env 加载、DI 装配、中间件管道与路由注册

using System.Text;
using System.Threading.RateLimiting;
using DotNetEnv;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using CNC_AgentCore.Api.Endpoints;
using CNC_AgentCore.Api.Middleware;
using CNC_AgentCore.Infrastructure;
using CNC_AgentCore.Infrastructure.Configuration;

// ===== .env 加载（向上搜：cwd / cwd 父级 / samples/ 等）=====
static string? FindEnvFile()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    for (var i = 0; i < 4 && dir is not null; i++, dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate)) return candidate;
        var samplesCandidate = Path.Combine(dir.FullName, "samples", ".env");
        if (File.Exists(samplesCandidate)) return samplesCandidate;
    }
    return null;
}
var envPath = FindEnvFile();
if (envPath is not null)
{
    Env.Load(envPath);
    Console.WriteLine($"[startup] loaded .env: {envPath}");
}
else
{
    Console.WriteLine("[startup] no .env found in cwd / parents / samples");
}

// ===== Kestrel + DI =====
var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// CORS（前端本地 dev 端口）
const string CorsPolicy = "FrontendDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",   // Vite dev
                "http://localhost:5174",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// JSON 源生成（AOT 友好）+ snake_case
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// ===== 应用服务注册 =====
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddJiebaTokenization();
builder.Services.AddLlmProviders(builder.Configuration);
builder.Services.AddAgentServices(builder.Configuration);
// JWT_SECRET 长度护栏：太短（< 32 bytes）时把默认占位补到 32，避免 HS256 启动失败
var rawSecret = builder.Configuration["JWT_SECRET"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? "";
if (Encoding.UTF8.GetByteCount(rawSecret) < 32)
{
    // 简单右填 '_' 到 ≥ 32 字符
    var padded = rawSecret.PadRight(32, '_');
    Environment.SetEnvironmentVariable("JWT_SECRET", padded);
    builder.Configuration["JWT_SECRET"] = padded;
}

builder.Services.AddJwtOptions(builder.Configuration);
builder.Services.AddAppHealthChecks();

// ===== 全局限流（按 IP 滑动窗口，60 req/min）=====
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsync("rate limit exceeded", ct);
    };
});

// ===== 中间件管道 =====
var app = builder.Build();

// 顺序：RequestLogging 最外层（finally 记最终状态码）→ TraceId → ApiErrorException 兜底
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ApiErrorExceptionMiddleware>();
app.UseRateLimiter();              // 全局限流
app.UseCors(CorsPolicy);
app.MapOpenApi();

// ===== 路由注册 =====
app.MapHealthEndpoints();
app.MapQueryEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapRolePermissionEndpoints();
app.MapFeedbackEndpoints();
app.MapStatsEndpoints();
app.MapTraceEndpoints();
app.MapVectorEndpoints();
app.MapWorkOrderEndpoints();
app.MapKnowledgeEndpoints();
app.MapDeviceEndpoints();
app.MapBaseItemEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    name = "CNC_AgentCore",
    version = "1.0.0",
    status = "running",
    dotnet = Environment.Version.ToString(),
}));

// ===== 启动 =====
app.Run();

// 让 WebApplicationFactory 可访问（集成测试用）
public partial class Program;
