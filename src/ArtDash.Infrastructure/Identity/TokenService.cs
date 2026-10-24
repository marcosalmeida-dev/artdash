using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ArtDash.Infrastructure.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ArtDash.Infrastructure.Identity;

/// <summary>Issues short-lived access tokens (JWT) and opaque refresh tokens.</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) CreateAccessToken(ApplicationUser user, IEnumerable<string> roles, string tenantSlug)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(Math.Max(1, _options.AccessTokenMinutes));

        var claims = new List<Claim>
        {
            new(ArtDashClaimTypes.Subject, user.Id.ToString()),
            new(ArtDashClaimTypes.Name, user.FullName),
            new(ArtDashClaimTypes.Email, user.Email ?? string.Empty),
            new(ArtDashClaimTypes.TenantId, user.TenantId.ToString()),
            new(ArtDashClaimTypes.TenantSlug, tenantSlug),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(roles.Select(role => new Claim(ArtDashClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public static string GenerateRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(48));

    /// <summary>Refresh tokens are stored hashed: a database leak does not leak live sessions.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static SymmetricSecurityKey SigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
