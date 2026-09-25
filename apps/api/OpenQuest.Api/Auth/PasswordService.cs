using System.Security.Cryptography;
using Isopoh.Cryptography.Argon2;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;

namespace OpenQuest.Api.Auth;

/// <summary>Hashing of passwords and recovery codes.</summary>
public interface IPasswordService
{
    string Hash(string secret);
    bool Verify(string hash, string secret);
    /// <summary>A valid hash of a throw-away secret, to verify against when the user does not exist (constant-ish timing).</summary>
    string DummyHash { get; }
}

/// <summary>Argon2id (PHC string format, salt included).</summary>
public class PasswordService(IOptions<AuthOptions> options) : IPasswordService
{
    private string? _dummy;
    public string DummyHash => _dummy ??= Hash("dummy-password-for-timing");

    // Unambiguous alphabet (no 0/O/1/I/L) for codes that players write down.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private readonly AuthOptions _o = options.Value;

    public string Hash(string secret)
        => Argon2.Hash(secret, _o.Argon2TimeCost, _o.Argon2MemoryKiB, _o.Argon2Parallelism, Argon2Type.HybridAddressing);

    public bool Verify(string hash, string secret)
    {
        try { return Argon2.Verify(hash, secret); }
        catch (Exception e) when (e is FormatException or ArgumentException) { return false; }
    }

    /// <summary>Random recovery code like "K7M2-QX4P-9ZTB" (12 symbols, ~59 bits).</summary>
    public static string NewRecoveryCode()
    {
        Span<char> c = stackalloc char[12];
        for (var i = 0; i < c.Length; i++) c[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return $"{new string(c[..4])}-{new string(c[4..8])}-{new string(c[8..])}";
    }

    /// <summary>Upper-cases and strips separators so players can type codes loosely.</summary>
    public static string NormalizeCode(string code)
        => new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
