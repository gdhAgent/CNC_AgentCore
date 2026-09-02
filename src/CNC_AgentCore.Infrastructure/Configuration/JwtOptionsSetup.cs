// Infrastructure/Configuration/JwtOptionsSetup.cs —— JWT 配置装配（读 jwt_* 扁平大写 env 键）
// JwtService 直接收 JwtOptions（非 IOptions<T>），故把实例注册为单例。
using CNC_AgentCore.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CNC_AgentCore.Infrastructure.Configuration;

public static class JwtOptionsSetup
{
    public static IServiceCollection AddJwtOptions(this IServiceCollection services, IConfiguration config)
    {
        var opts = new JwtOptions
        {
            Secret = config["JWT_SECRET"] ?? string.Empty,
            TtlSec = int.TryParse(config["JWT_TTL_SEC"], out var ttl) ? ttl : 86400,
            Issuer = config["JWT_ISSUER"] ?? "cnc-agent-core",
            Algorithm = config["JWT_ALGORITHM"] ?? "HS256",
        };
        services.AddSingleton(opts);
        return services;
    }
}
