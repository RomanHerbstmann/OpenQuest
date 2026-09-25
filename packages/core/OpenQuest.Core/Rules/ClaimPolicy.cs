namespace OpenQuest.Core.Rules;

public static class ClaimPolicy
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public static DateTimeOffset ExpiresAt(DateTimeOffset claimedAt, TimeSpan? timeout = null)
        => claimedAt + (timeout ?? DefaultTimeout);

    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now >= expiresAt;
}
