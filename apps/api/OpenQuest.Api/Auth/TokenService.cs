using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using JwtClaim = System.Security.Claims.Claim;

namespace OpenQuest.Api.Auth;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) Create(User user);
}

public class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _o = options.Value;

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));

    public static string RoleName(UserRole role) => role.ToString().ToLowerInvariant();

    public (string Token, DateTimeOffset ExpiresAt) Create(User user)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(_o.ExpiryMinutes);
        var claims = new JwtClaim[]
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new("role", RoleName(user.Role)),
        };
        var token = new JwtSecurityToken(
            _o.Issuer, _o.Audience, claims,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey(_o), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal p)
    {
        var sub = p.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(sub, out var id) ? id : throw new InvalidOperationException("Token has no valid user id.");
    }
}
