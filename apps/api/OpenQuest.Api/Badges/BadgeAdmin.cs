using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Badges;

/// <summary>What players see of the badges, and what admins can change.</summary>
public interface IBadgeDirectory
{
    /// <summary>The active badges with the player's progress; the earned ones first (newest first), then the closest ones.</summary>
    Task<IReadOnlyList<PlayerBadgeDto>> ForPlayerAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<BadgeDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<ServiceResult<BadgeDto>> CreateAsync(BadgeRequest req, CancellationToken ct);
    Task<ServiceResult<BadgeDto>> UpdateAsync(Guid id, BadgeRequest req, CancellationToken ct);
}

public sealed class BadgeDirectory(AppDbContext db, IBadgeService badges, TimeProvider clock) : IBadgeDirectory
{
    public async Task<IReadOnlyList<PlayerBadgeDto>> ForPlayerAsync(Guid userId, CancellationToken ct)
    {
        var all = await db.Badges.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        var earned = await db.UserBadges.AsNoTracking().Where(u => u.UserId == userId).ToDictionaryAsync(u => u.BadgeId, u => u.AwardedAt, ct);
        var stats = await badges.StatsAsync(userId, ct);
        return all.Select(b =>
            {
                var criteria = BadgeRules.FromJson(b.Criteria) ?? new BadgeCriteria("", 1);
                var progress = BadgeRules.Progress(criteria, stats);
                var isEarned = earned.TryGetValue(b.Id, out var at);
                // an earned badge shows full progress even if the numbers have gone down since (a later rejection, a changed criteria)
                return new PlayerBadgeDto(b.Id, b.Key, b.Name, b.Description, b.Icon, criteria, b.RewardPoints, isEarned, isEarned ? at : null,
                    isEarned ? new BadgeProgressDto(Math.Max(progress.Current, progress.Required), progress.Required, 100)
                             : new BadgeProgressDto(progress.Current, progress.Required, progress.Percent));
            })
            .OrderByDescending(b => b.Earned).ThenByDescending(b => b.AwardedAt).ThenByDescending(b => b.Progress.Percent).ThenBy(b => b.Key)
            .ToList();
    }

    public async Task<IReadOnlyList<BadgeDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = await db.Badges.AsNoTracking().Where(b => includeInactive || b.IsActive).OrderBy(b => b.Key)
            .Select(b => new { Badge = b, Holders = db.UserBadges.Count(u => u.BadgeId == b.Id) }).ToListAsync(ct);
        return rows.Select(x => ToDto(x.Badge, x.Holders)).ToList();
    }

    public async Task<ServiceResult<BadgeDto>> CreateAsync(BadgeRequest req, CancellationToken ct)
    {
        var errors = Validate(req, creating: true);
        if (errors.Count > 0) return Invalid(errors);
        var key = string.IsNullOrWhiteSpace(req.Key) ? Slug.From(req.Name!) : req.Key.Trim().ToLowerInvariant();
        if (key.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9][a-z0-9_-]{0,63}$"))
            return Invalid(new() { ["key"] = ["Lowercase letters, digits, '_' and '-', at most 64 characters."] });
        if (await db.Badges.AnyAsync(b => b.Key == key, ct)) return ServiceResult<BadgeDto>.Fail(409, "key_taken", "A badge with this key exists.");

        var now = clock.GetUtcNow();
        var badge = new Badge
        {
            Key = key, Name = req.Name!.Trim(), Description = req.Description?.Trim() ?? "", Icon = string.IsNullOrWhiteSpace(req.Icon) ? "trophy" : req.Icon.Trim(),
            Criteria = BadgeRules.ToJson(req.Criteria!), RewardPoints = req.RewardPoints ?? 0, IsActive = req.IsActive ?? true, CreatedAt = now, UpdatedAt = now,
        };
        db.Badges.Add(badge);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        if (badge.IsActive) await badges.EvaluateAllAsync(ct);   // players who have already reached it get it now
        return ServiceResult<BadgeDto>.Success(ToDto(badge, await db.UserBadges.CountAsync(u => u.BadgeId == badge.Id, ct)));
    }

    public async Task<ServiceResult<BadgeDto>> UpdateAsync(Guid id, BadgeRequest req, CancellationToken ct)
    {
        var badge = await db.Badges.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (badge is null) return ServiceResult<BadgeDto>.Fail(404, "badge_not_found");
        var errors = Validate(req, creating: false);
        if (req.Key is not null && !string.Equals(req.Key.Trim(), badge.Key, StringComparison.OrdinalIgnoreCase)) errors["key"] = ["The key of a badge never changes."];
        if (errors.Count > 0) return Invalid(errors);

        var wasActive = badge.IsActive;
        var oldCriteria = badge.Criteria;
        if (req.Name is not null) badge.Name = req.Name.Trim();
        if (req.Description is not null) badge.Description = req.Description.Trim();
        if (!string.IsNullOrWhiteSpace(req.Icon)) badge.Icon = req.Icon.Trim();
        if (req.Criteria is not null) badge.Criteria = BadgeRules.ToJson(req.Criteria);
        if (req.RewardPoints is { } points) badge.RewardPoints = points;
        if (req.IsActive is { } active) badge.IsActive = active;
        badge.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var reevaluate = badge.IsActive && (!wasActive || oldCriteria != badge.Criteria);
        db.ChangeTracker.Clear();
        if (reevaluate) await badges.EvaluateAllAsync(ct);
        var fresh = await db.Badges.AsNoTracking().FirstAsync(b => b.Id == id, ct);
        return ServiceResult<BadgeDto>.Success(ToDto(fresh, await db.UserBadges.CountAsync(u => u.BadgeId == id, ct)));
    }

    private static Dictionary<string, string[]> Validate(BadgeRequest req, bool creating)
    {
        var errors = new Dictionary<string, string[]>();
        if (creating && string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["A name is required."];
        if (req.Name is not null && (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 200)) errors["name"] = ["1 to 200 characters."];
        if (req.Description is { Length: > 500 }) errors["description"] = ["At most 500 characters."];
        if (req.Icon is { Length: > 64 }) errors["icon"] = ["At most 64 characters."];
        if (creating && req.Criteria is null) errors["criteria"] = ["Criteria are required, for example { \"type\": \"approved_submissions\", \"count\": 10 }."];
        if (req.Criteria is not null && BadgeRules.Validate(req.Criteria) is { Count: > 0 } problems) errors["criteria"] = [.. problems];
        if (req.RewardPoints is < 0 or > 100_000) errors["rewardPoints"] = ["Between 0 and 100000."];
        return errors;
    }

    private static ServiceResult<BadgeDto> Invalid(Dictionary<string, string[]> errors)
        => ServiceResult<BadgeDto>.Fail(400, "validation_failed", "The request is not valid.", errors);

    private static BadgeDto ToDto(Badge b, int holders)
        => new(b.Id, b.Key, b.Name, b.Description, b.Icon, BadgeRules.FromJson(b.Criteria) ?? new BadgeCriteria("", 1), b.RewardPoints, b.IsActive, holders);
}
