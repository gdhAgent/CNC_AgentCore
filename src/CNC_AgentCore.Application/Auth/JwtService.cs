// Application/Auth/JwtService.cs —— JWT 签发/解码。
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.ValueObjects;
using Microsoft.IdentityModel.Tokens;

namespace CNC_AgentCore.Application.Auth;

public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public int TtlSec { get; set; } = 86400;
    public string Issuer { get; set; } = "cnc-agent-core";
    public string Algorithm { get; set; } = "HS256";
}

public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _opts;

    static JwtService()
    {
        // 关闭默认 inbound claim 改名：否则 "uid"/"role"/"name" 会被映射成 ClaimTypes.* 标准类型，FindFirst 查不到
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
    }

    public JwtService(JwtOptions opts) => _opts = opts;

    public string IssueToken(long uid, string username, string role, string displayName)
    {
        if (string.IsNullOrEmpty(_opts.Secret))
            throw new InvalidOperationException("JWT secret 未配置");
        // HS256 要求 key ≥ 256 bits（32 bytes），.NET IdentityModel 硬校验
        if (Encoding.UTF8.GetByteCount(_opts.Secret) < 32)
            throw new InvalidOperationException(
                $"JWT secret 太短 ({Encoding.UTF8.GetByteCount(_opts.Secret)} bytes)；HS256 至少需要 32 bytes。" +
                "请在 .env 里把 JWT_SECRET 设成 ≥ 32 字符");
        if (uid <= 0) throw new ArgumentException("uid 必须 > 0");
        if (role is not ("admin" or "operator" or "viewer"))
            throw new ArgumentException($"invalid role: {role}");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        // 按配置选 HMAC 算法（HS256/HS384/HS512）
        var algo = (_opts.Algorithm ?? "HS256").ToUpperInvariant() switch
        {
            "HS512" => SecurityAlgorithms.HmacSha512,
            "HS384" => SecurityAlgorithms.HmacSha384,
            _ => SecurityAlgorithms.HmacSha256,   // 默认 + HS256 兼容
        };
        var creds = new SigningCredentials(key, algo);

        var claims = new[]
        {
            new Claim("iss", _opts.Issuer),
            new Claim("sub", uid.ToString()),
            new Claim("uid", uid.ToString()),
            new Claim("username", username ?? ""),
            new Claim("role", role),
            new Claim("name", displayName ?? ""),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Exp, (now + _opts.TtlSec).ToString(), ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(claims: claims, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenPayload DecodeToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new JwtInvalidError("token must be non-empty str");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Secret));
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var validation = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _opts.Issuer,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromSeconds(30),
                RequireExpirationTime = true,
                RequireSignedTokens = true,
            };
            var principal = handler.ValidateToken(token, validation, out _);
            var uidClaim = principal.FindFirst("uid")?.Value
                ?? throw new JwtInvalidError("token payload missing uid");
            var username = principal.FindFirst("username")?.Value ?? "";
            var role = principal.FindFirst("role")?.Value ?? "";
            var name = principal.FindFirst("name")?.Value ?? "";
            if (role is not ("admin" or "operator" or "viewer"))
                throw new JwtInvalidError($"invalid role in token: {role}");

            return new TokenPayload
            {
                Uid = long.Parse(uidClaim),
                Username = username,
                Role = role,
                DisplayName = name,
                Iat = long.Parse(principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value ?? "0"),
                Exp = long.Parse(principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value ?? "0"),
            };
        }
        catch (SecurityTokenExpiredException ex)
        {
            throw new JwtExpiredError($"token expired: {ex.Message}");
        }
        catch (SecurityTokenException ex)
        {
            throw new JwtInvalidError($"token invalid: {ex.Message}");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException
                                     or FormatException
                                     or ArgumentException)
        {
            // 篡改 token 可能让 base64 解码后 JSON 不合法 → 转 JwtInvalidError
            throw new JwtInvalidError($"token malformed: {ex.Message}");
        }
    }

    public TokenPayload? SafeDecode(string token)
    {
        try { return DecodeToken(token); }
        catch (JwtError) { return null; }
    }

    public int RemainingSeconds(TokenPayload payload)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Math.Max(0, (int)(payload.Exp - now));
    }
}
