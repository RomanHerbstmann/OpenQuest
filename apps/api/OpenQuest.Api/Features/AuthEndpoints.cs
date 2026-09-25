using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Services;

namespace OpenQuest.Api.Features;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(RateLimitPolicy);

        g.MapPost("/register", async (Credentials c, IUserRegistration users, CancellationToken ct) =>
                (await users.RegisterAsync(c, ct)).ToHttp(r => Results.Created("/me", r)))
            .WithName("Register")
            .WithSummary("Creates a player account (username + password, no email). The response contains one-time recovery codes: shown only once.")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        g.MapPost("/login", async (Credentials c, IUserAuthentication auth, CancellationToken ct) =>
                (await auth.LoginAsync(c, ct)).ToHttp(Results.Ok))
            .WithName("Login")
            .Produces<AuthResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        g.MapPost("/recover", async (RecoverRequest r, IAccountRecovery recovery, CancellationToken ct) =>
                (await recovery.RecoverAsync(r, ct)).ToHttp(Results.Ok))
            .WithName("RecoverAccount")
            .WithSummary("Sets a new password using one of the recovery codes shown at sign-up. Each code works once.")
            .Produces<RecoverResponse>();

        app.MapPost("/me/recovery-codes", async (PasswordRequest r, ClaimsPrincipal user, IAccountRecovery recovery, CancellationToken ct) =>
                (await recovery.RegenerateCodesAsync(user.GetUserId(), r.Password, ct)).ToHttp(Results.Ok))
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicy).WithTags("Auth")
            .WithName("RegenerateRecoveryCodes")
            .WithSummary("Replaces all recovery codes with a fresh set (requires the current password). The codes are shown only once.");
    }
}
