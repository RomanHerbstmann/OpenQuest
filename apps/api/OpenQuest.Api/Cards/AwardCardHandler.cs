using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Api.Districts;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Cards;

/// <summary>
/// Hands out a tree card when a submission is approved. The card shows the tree's genus (or, for a genus quest, the genus the
/// player found out); how rare it is depends on how frequent that genus is in the tree's district and on whether the submission
/// delivered new information (<see cref="CardRarity"/>). No genus known and none found out: no card.
/// Idempotent, because delivery is at-least-once: one card per submission (unique index), and the dice roll only depends on the submission.
/// </summary>
public sealed class AwardCardHandler(
    AppDbContext db, IDistrictLocator districts, IDistrictGenusStats stats, GamificationProfile profile, TimeProvider clock)
    : IEventHandler<SubmissionApproved>
{
    private static readonly HashSet<string> ProblemConditions = ["damaged", "dead", "gone"];

    public async Task HandleAsync(IReadOnlyList<SubmissionApproved> events, CancellationToken ct)
    {
        var ids = events.Select(e => e.SubmissionId).Distinct().ToList();
        var done = (await db.Cards.AsNoTracking().Where(c => ids.Contains(c.SubmissionId)).Select(c => c.SubmissionId).ToListAsync(ct)).ToHashSet();
        var todo = ids.Where(id => !done.Contains(id)).ToList();
        if (todo.Count == 0) return;

        // a quest without asset (a tree that is missing in the data) gives no card: there is no tree to compare with the district's statistics
        var submissions = await db.Submissions.AsNoTracking().Where(s => todo.Contains(s.Id) && s.Claim.Quest.AssetId != null)
            .Select(s => new
            {
                s.Id, s.Payload, s.Claim.UserId, TaskType = s.Claim.Quest.TaskType.Key, s.Claim.Quest.TaskConfig,
                AssetId = s.Claim.Quest.AssetId!.Value, AssetAttributes = s.Claim.Quest.Asset!.Attributes,
            }).ToListAsync(ct);

        var now = clock.GetUtcNow();
        foreach (var s in submissions)
        {
            var known = GenusName.Normalize(Text(JsonNode.Parse(s.AssetAttributes), "genus"));
            var payload = JsonNode.Parse(s.Payload);
            var isGenusQuest = s.TaskType == "verify_attribute" && Text(JsonNode.Parse(s.TaskConfig), "attribute") == "genus";
            var found = isGenusQuest ? GenusName.Normalize(Text(payload, "value")) : null;

            var genus = found ?? known;
            if (genus is null) continue; // nothing to put on a card

            // the data did not have this genus (no genus, or another one): the player brought new information
            var newInformation = found is not null && !string.Equals(found, known, StringComparison.OrdinalIgnoreCase);
            // a problem with the tree is a fact worth a boost: a bad condition, or any reported issue (root lift, damage, ...)
            var conditionFact = s.TaskType == "condition_report"
                                && ((Text(payload, "condition") is { } c && ProblemConditions.Contains(c)) || payload?["issues"] is JsonArray { Count: > 0 });

            var districtId = await districts.FindForAssetAsync(s.AssetId, ct);
            var share = districtId is null ? null : await stats.GetShareAsync(districtId.Value, genus, ct);
            var result = CardRarity.Decide(
                new RarityInput(share?.Share, share?.Sample ?? 0, newInformation, conditionFact), CardRarity.RollFor(s.Id), profile.Rarity);

            db.Cards.Add(new Card
            {
                UserId = s.UserId, SubmissionId = s.Id, AssetId = s.AssetId, DistrictId = districtId, Genus = genus,
                Rarity = result.Rarity, Frequency = result.Frequency, Share = share?.Share,
                Reasons = JsonSerializer.Serialize(result.Reasons), CreatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string? Text(JsonNode? node, string name) => node?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
