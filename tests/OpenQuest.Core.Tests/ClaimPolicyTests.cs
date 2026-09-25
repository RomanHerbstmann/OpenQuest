using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class ClaimPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiresAt_uses_default_timeout()
        => Assert.Equal(T0 + ClaimPolicy.DefaultTimeout, ClaimPolicy.ExpiresAt(T0));

    [Fact]
    public void IsExpired_is_true_at_and_after_deadline()
    {
        var exp = ClaimPolicy.ExpiresAt(T0, TimeSpan.FromMinutes(10));
        Assert.False(ClaimPolicy.IsExpired(exp, T0.AddMinutes(9)));
        Assert.True(ClaimPolicy.IsExpired(exp, T0.AddMinutes(10)));
        Assert.True(ClaimPolicy.IsExpired(exp, T0.AddMinutes(11)));
    }
}
