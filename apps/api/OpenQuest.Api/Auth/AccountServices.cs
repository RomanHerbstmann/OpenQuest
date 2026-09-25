using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Auth;

public interface IUserRegistration
{
    Task<ServiceResult<RegisterResponse>> RegisterAsync(Credentials credentials, CancellationToken ct);
}

public interface IUserAuthentication
{
    Task<ServiceResult<AuthResponse>> LoginAsync(Credentials credentials, CancellationToken ct);
}

public interface IAccountRecovery
{
    Task<ServiceResult<RecoverResponse>> RecoverAsync(RecoverRequest request, CancellationToken ct);
    Task<ServiceResult<RecoveryCodesResponse>> RegenerateCodesAsync(Guid userId, string? password, CancellationToken ct);
}

/// <summary>Creates and stores the one-time recovery codes of a user (shown to the player once).</summary>
public interface IRecoveryCodeIssuer
{
    /// <summary>Adds new codes to the context (caller saves) and returns them in plain text.</summary>
    List<string> Issue(Guid userId);
}

public sealed class RecoveryCodeIssuer(AppDbContext db, IPasswordService passwords) : IRecoveryCodeIssuer
{
    public const int Count = 8;

    public List<string> Issue(Guid userId)
    {
        var codes = new List<string>(Count);
        for (var i = 0; i < Count; i++)
        {
            var code = PasswordService.NewRecoveryCode();
            codes.Add(code);
            db.UserRecoveryCodes.Add(new UserRecoveryCode { UserId = userId, CodeHash = passwords.Hash(PasswordService.NormalizeCode(code)) });
        }
        return codes;
    }
}

public sealed partial class UserRegistration(AppDbContext db, IPasswordService passwords, IRecoveryCodeIssuer codes, ITokenService tokens)
    : IUserRegistration
{
    [GeneratedRegex("^[a-z0-9_.-]{3,32}$")]
    private static partial Regex UsernamePattern();

    public async Task<ServiceResult<RegisterResponse>> RegisterAsync(Credentials c, CancellationToken ct)
    {
        var username = c.Username?.Trim().ToLowerInvariant() ?? "";
        if (!UsernamePattern().IsMatch(username))
            return Invalid("username", "3-32 characters: a-z, 0-9, '_', '.', '-'.");
        if (!PasswordRules.IsValid(c.Password))
            return Invalid("password", PasswordRules.Message);
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return ServiceResult<RegisterResponse>.Fail(409, "username_taken");

        var user = new User { Username = username, Role = UserRole.Player, PasswordHash = passwords.Hash(c.Password!) };
        db.Users.Add(user);
        var recoveryCodes = codes.Issue(user.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return ServiceResult<RegisterResponse>.Fail(409, "username_taken"); }

        var (token, exp) = tokens.Create(user);
        return ServiceResult<RegisterResponse>.Success(new RegisterResponse(token, exp, user.Username, TokenService.RoleName(user.Role), recoveryCodes));
    }

    private static ServiceResult<RegisterResponse> Invalid(string field, string message)
        => ServiceResult<RegisterResponse>.Fail(400, "validation_failed", message, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class PasswordAuthentication(AppDbContext db, IPasswordService passwords, ITokenService tokens, TimeProvider clock)
    : IUserAuthentication
{
    public async Task<ServiceResult<AuthResponse>> LoginAsync(Credentials c, CancellationToken ct)
    {
        var username = c.Username?.Trim().ToLowerInvariant() ?? "";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.DeletedAt == null, ct);
        // Always run one hash verification so timing does not reveal whether the username exists.
        var ok = passwords.Verify(user?.PasswordHash ?? passwords.DummyHash, c.Password ?? "") && user is not null;
        if (!ok) return ServiceResult<AuthResponse>.Fail(401, "invalid_credentials");

        user!.LastLoginAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var (token, exp) = tokens.Create(user);
        return ServiceResult<AuthResponse>.Success(new AuthResponse(token, exp, user.Username, TokenService.RoleName(user.Role)));
    }
}

public sealed class AccountRecovery(
    AppDbContext db, IPasswordService passwords, IRecoveryCodeIssuer codes, ITokenService tokens, TimeProvider clock) : IAccountRecovery
{
    public async Task<ServiceResult<RecoverResponse>> RecoverAsync(RecoverRequest r, CancellationToken ct)
    {
        var username = r.Username?.Trim().ToLowerInvariant() ?? "";
        if (!PasswordRules.IsValid(r.NewPassword))
            return ServiceResult<RecoverResponse>.Fail(400, "validation_failed", PasswordRules.Message,
                new Dictionary<string, string[]> { ["newPassword"] = [PasswordRules.Message] });
        if (string.IsNullOrWhiteSpace(r.RecoveryCode))
            return ServiceResult<RecoverResponse>.Fail(400, "validation_failed", "Recovery code required.",
                new Dictionary<string, string[]> { ["recoveryCode"] = ["Required."] });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.DeletedAt == null, ct);
        var code = PasswordService.NormalizeCode(r.RecoveryCode);
        UserRecoveryCode? match = null;
        if (user is not null)
        {
            var open = await db.UserRecoveryCodes.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync(ct);
            match = open.FirstOrDefault(x => passwords.Verify(x.CodeHash, code));
        }
        else passwords.Verify(passwords.DummyHash, code);

        if (user is null || match is null) return ServiceResult<RecoverResponse>.Fail(401, "invalid_recovery");

        var now = clock.GetUtcNow();
        match.UsedAt = now; // single use
        user.PasswordHash = passwords.Hash(r.NewPassword!);
        user.LastLoginAt = now;
        await db.SaveChangesAsync(ct);

        var remaining = await db.UserRecoveryCodes.CountAsync(x => x.UserId == user.Id && x.UsedAt == null, ct);
        var (token, exp) = tokens.Create(user);
        return ServiceResult<RecoverResponse>.Success(new RecoverResponse(token, exp, user.Username, TokenService.RoleName(user.Role), remaining));
    }

    public async Task<ServiceResult<RecoveryCodesResponse>> RegenerateCodesAsync(Guid userId, string? password, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null || string.IsNullOrEmpty(password) || !passwords.Verify(user.PasswordHash, password))
            return ServiceResult<RecoveryCodesResponse>.Fail(401, "invalid_credentials");

        await db.UserRecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        var fresh = codes.Issue(userId);
        await db.SaveChangesAsync(ct);
        return ServiceResult<RecoveryCodesResponse>.Success(new RecoveryCodesResponse(fresh));
    }
}

public static class PasswordRules
{
    public const string Message = "8-128 characters required.";
    public static bool IsValid(string? password) => password is { Length: >= 8 and <= 128 };
}
