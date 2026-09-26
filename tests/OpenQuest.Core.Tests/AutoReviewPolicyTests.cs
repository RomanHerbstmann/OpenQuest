using OpenQuest.Core.Review;

namespace OpenQuest.Core.Tests;

public class AutoReviewPolicyTests
{
    [Theory]
    [InlineData(AutoReviewVerdict.Approve, true)]
    [InlineData(AutoReviewVerdict.Review, false)]
    [InlineData(AutoReviewVerdict.Reject, false)]   // nothing is ever rejected automatically, and a bad photo is no reason to approve
    public void Only_a_clear_approval_acts(AutoReviewVerdict verdict, bool approves)
        => Assert.Equal(approves, AutoReviewPolicy.ShouldApprove(new AutoReviewDecision(verdict, [])));
}
