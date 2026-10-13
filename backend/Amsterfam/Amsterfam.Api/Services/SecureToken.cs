using System.Security.Cryptography;
using System.Text;

namespace Amsterfam.Api.Services;

/// <summary>Random URL-safe tokens for links (join links, feed links).</summary>
public static class SecureToken
{
    /// <summary>32 random bytes, base64url without padding (43 characters).</summary>
    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lower-case hex SHA-256 of the token, for tokens stored hashed.</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
