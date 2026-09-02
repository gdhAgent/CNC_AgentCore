// Application/Auth/PasswordHasher.cs —— PBKDF2-SHA256 口令哈希/校验。
using System.Security.Cryptography;
using CNC_AgentCore.Domain.Abstractions;

namespace CNC_AgentCore.Application.Auth;

public sealed class PasswordHasher : IPasswordHasher
{
    public const string Scheme = "pbkdf2_sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    public const int DefaultIterations = 100_000;

    public string EncodeHash(string password, int? iterations = null)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("password 不能为空", nameof(password));
        var iter = iterations ?? DefaultIterations;
        if (iter < 1000) throw new ArgumentException($"iterations too low ({iter}); use >= 1000");

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password: System.Text.Encoding.UTF8.GetBytes(password),
            salt: salt,
            iterations: iter,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashBytes);

        return $"{Scheme}${iter}${B64Encode(salt)}${B64Encode(derived)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], out var iter)) return false;

        byte[] salt, expectedHash;
        try
        {
            salt = B64Decode(parts[2]);
            expectedHash = B64Decode(parts[3]);
        }
        catch { return false; }

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password: System.Text.Encoding.UTF8.GetBytes(password),
            salt: salt,
            iterations: iter,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashBytes);

        return CryptographicOperations.FixedTimeEquals(derived, expectedHash);
    }

    public bool NeedsRehash(string storedHash, int? currentIterations = null)
    {
        var cur = currentIterations ?? DefaultIterations;
        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Scheme) return true;
            if (!int.TryParse(parts[1], out var iter)) return true;
            return iter < cur;
        }
        catch { return true; }
    }

    private static string B64Encode(byte[] raw) =>
        Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] B64Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        var pad = (4 - s.Length % 4) % 4;
        return Convert.FromBase64String(s + new string('=', pad));
    }
}
