using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Review;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.AutoReview;

/// <summary>
/// Checks a fresh photo submission automatically (phase 6) and approves it when the check is sure. What happens:
/// the verdict and reasons are stored with the submission (<c>auto_review</c>, the moderator sees them next to the photo); only a clear
/// <see cref="AutoReviewVerdict.Approve"/> approves it, through the same review service a moderator uses (<c>reviewed_by</c> stays empty), so points,
/// cards and publication follow as always. <c>Review</c> and <c>Reject</c> leave it pending: nothing is ever rejected automatically.
/// Does nothing unless a checker is registered (<c>AutoReview:Enabled</c>). Idempotent: a submission that was checked is not checked again.
/// </summary>
public sealed class AutoReviewHandler(
    AppDbContext db, IEnumerable<ISubmissionAutoReviewer> reviewers, ISubmissionReviewService reviews, IBlobReader blobs,
    IOptions<AutoReviewOptions> options, TimeProvider clock, ILogger<AutoReviewHandler> log) : IEventHandler<SubmissionSubmitted>
{
    public async Task HandleAsync(IReadOnlyList<SubmissionSubmitted> events, CancellationToken ct)
    {
        var reviewer = reviewers.FirstOrDefault();
        if (reviewer is null || !options.Value.Enabled) return;

        var eligible = events.Where(e => e.HasPhoto && options.Value.TaskTypes.Contains(e.TaskType, StringComparer.OrdinalIgnoreCase))
            .GroupBy(e => e.SubmissionId).Select(g => g.First()).ToList();
        var failures = new List<Exception>();
        foreach (var e in eligible)
        {
            try { await HandleOneAsync(reviewer, e.SubmissionId, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failures.Add(ex);   // the others are still done; the outbox retries the batch, finished ones are skipped
                db.ChangeTracker.Clear();
            }
        }
        if (failures.Count > 0) throw new AggregateException("The automatic check failed for some submissions.", failures);
    }

    private async Task HandleOneAsync(ISubmissionAutoReviewer reviewer, Guid submissionId, CancellationToken ct)
    {
        var submission = await db.Submissions.Include(s => s.Claim).ThenInclude(c => c.Quest).ThenInclude(q => q.Asset)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null || submission.Status != SubmissionStatus.Pending) return;   // gone, or a moderator was faster

        var decision = submission.AutoReview is null ? await CheckAsync(reviewer, submission, ct) : Stored(submission.AutoReview);
        if (decision is null) return;   // no photo to look at

        if (submission.AutoReview is null)
        {
            submission.AutoReview = new JsonObject
            {
                ["verdict"] = decision.Verdict.ToString().ToLowerInvariant(),
                ["reasons"] = new JsonArray(decision.Reasons.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
                ["details"] = decision.DetailsJson is null ? null : Parse(decision.DetailsJson),
            }.ToJsonString();
            submission.AutoReviewedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        if (!AutoReviewPolicy.ShouldApprove(decision)) return;
        // The check may have taken a while and a moderator may have decided meanwhile: the review service must read the submission afresh
        // (it would otherwise get the pending copy tracked here and approve a submission that was rejected in the meantime).
        db.ChangeTracker.Clear();
        var result = await reviews.ReviewAsync(null, submissionId, approved: true, reason: null, ct);
        if (!result.Ok && result.Error!.Code != "already_reviewed") throw new InvalidOperationException($"Approving {submissionId} failed: {result.Error.Code}");
        if (result.Ok) log.LogInformation("Submission {Id} was approved by the automatic check.", submissionId);
    }

    private async Task<AutoReviewDecision?> CheckAsync(ISubmissionAutoReviewer reviewer, Submission submission, CancellationToken ct)
    {
        var media = await db.Media.AsNoTracking().FirstOrDefaultAsync(m => m.SubmissionId == submission.Id, ct);
        if (media is null) return null;
        var image = await blobs.GetAsync(media.StorageKey, ct);
        if (image is null) return null;

        var asset = submission.Claim.Quest.Asset;
        var genus = asset is null ? null : GenusName.Normalize(JsonNode.Parse(asset.Attributes)?["genus"] is JsonValue v && v.TryGetValue<string>(out var g) ? g : null);
        return await reviewer.ReviewAsync(new AutoReviewRequest(
            image, media.MimeType, new GeoPoint(submission.Location.Y, submission.Location.X), null,
            asset is null ? null : new GeoPoint(asset.Geom.Y, asset.Geom.X), genus, media.CapturedAt), ct);
    }

    private static AutoReviewDecision Stored(string json)
    {
        var node = JsonNode.Parse(json);
        var verdict = Enum.TryParse<AutoReviewVerdict>(node?["verdict"]?.GetValue<string>(), ignoreCase: true, out var v) ? v : AutoReviewVerdict.Review;
        var reasons = node?["reasons"] is JsonArray a ? a.Select(r => r?.GetValue<string>() ?? "").ToList() : [];
        return new AutoReviewDecision(verdict, reasons);
    }

    private static JsonNode? Parse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return JsonValue.Create(json); }
    }
}
