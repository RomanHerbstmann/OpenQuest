using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Api.Publishing;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Services;

public interface ISubmissionReviewService
{
    Task<ServiceResult<SubmissionStatus>> ReviewAsync(Guid? reviewerId, Guid submissionId, bool approved, string? reason, CancellationToken ct);
}

public sealed class SubmissionReviewService(
    AppDbContext db, IQuestSlotLedger ledger, IEventPublisher events, TimeProvider clock) : ISubmissionReviewService
{
    /// <summary>
    /// Approves or rejects a pending submission. Approval accepts the proposed attribute changes and publishes
    /// <see cref="AttributeChangeAccepted"/> events in the same transaction, so accepted data is guaranteed to reach
    /// the open data channels; a proposed new tree is accepted and published the same way. Rejection discards the changes and frees the
    /// quest slot (the player may try again). <paramref name="reviewerId"/> is null when the automatic check approved it.
    /// </summary>
    public async Task<ServiceResult<SubmissionStatus>> ReviewAsync(
        Guid? reviewerId, Guid submissionId, bool approved, string? reason, CancellationToken ct)
    {
        if (!approved && string.IsNullOrWhiteSpace(reason))
            return ServiceResult<SubmissionStatus>.Fail(422, "reason_required", "A reason is required when rejecting.");

        var questId = await db.Submissions.Where(s => s.Id == submissionId).Select(s => (Guid?)s.Claim.QuestId).FirstOrDefaultAsync(ct);
        if (questId is null) return ServiceResult<SubmissionStatus>.Fail(404, "submission_not_found");

        // Lock order: quest first, then submission (same as claim/cancel), so reviews cannot deadlock with claims.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockQuestAsync(questId.Value, ct);
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM submission WHERE id = {submissionId} FOR UPDATE").ToListAsync(ct);

        var submission = await db.Submissions.Include(s => s.Claim).ThenInclude(c => c.Quest).FirstAsync(s => s.Id == submissionId, ct);
        if (submission.Status != SubmissionStatus.Pending)
            return ServiceResult<SubmissionStatus>.Fail(409, "already_reviewed");

        var now = clock.GetUtcNow();
        var quest = submission.Claim.Quest;
        submission.Status = approved ? SubmissionStatus.Approved : SubmissionStatus.Rejected;
        submission.ReviewedBy = reviewerId;
        submission.ReviewedAt = now;
        submission.RejectionReason = approved ? null : reason!.Trim();

        var changes = await db.AttributeChanges
            .Include(c => c.Asset).ThenInclude(a => a.AssetType)
            .Where(c => c.SubmissionId == submissionId && c.Status == ChangeStatus.Proposed).ToListAsync(ct);

        var proposals = await db.AssetProposals.Include(p => p.Submission)
            .Where(p => p.SubmissionId == submissionId && p.Status == ChangeStatus.Proposed).ToListAsync(ct);

        if (approved)
        {
            var sourceKeys = await db.DataSources.AsNoTracking()
                .Where(d => changes.Select(c => c.Asset.DataSourceId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Key, ct);
            foreach (var c in changes)
            {
                c.Status = ChangeStatus.Accepted;
                c.Submission = submission;
                events.Publish(new AttributeChangeAccepted(ApprovedContributionMapper.From(c, sourceKeys[c.Asset.DataSourceId])));
            }
            if (proposals.Count > 0)
            {
                var proposalSources = await db.DataSources.AsNoTracking()
                    .Where(d => proposals.Select(p => p.DataSourceId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Key, ct);
                var treeType = AssetType.Tree.Key;
                foreach (var p in proposals)
                {
                    p.Status = ChangeStatus.Accepted;
                    events.Publish(new AttributeChangeAccepted(ApprovedContributionMapper.From(p, proposalSources[p.DataSourceId], treeType)));
                }
            }
            events.Publish(new SubmissionApproved(submissionId, submission.Claim.UserId, quest.Id, quest.RewardPoints));
        }
        else
        {
            foreach (var c in changes) c.Status = ChangeStatus.Discarded;
            foreach (var p in proposals) p.Status = ChangeStatus.Discarded;
            submission.Claim.Status = ClaimStatus.Cancelled; // lets the player try again
            ledger.ChangeSlots(quest, -1);
            events.Publish(new SubmissionRejected(submissionId, submission.Claim.UserId, quest.Id, submission.RejectionReason!));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<SubmissionStatus>.Success(submission.Status);
    }
}
