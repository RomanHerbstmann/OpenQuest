using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Cards;

/// <summary>Read side: a player's cards and tree book.</summary>
public interface ICardCollection
{
    Task<IReadOnlyList<CardDto>> ListAsync(Guid userId, int offset, int limit, CancellationToken ct);
    Task<CollectionDto> CollectionAsync(Guid userId, CancellationToken ct);
}

public sealed class CardCollection(AppDbContext db) : ICardCollection
{
    public async Task<IReadOnlyList<CardDto>> ListAsync(Guid userId, int offset, int limit, CancellationToken ct)
    {
        var rows = await db.Cards.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Skip(offset).Take(limit)
            .Select(c => new
            {
                Card = c,
                DistrictName = db.Districts.Where(d => d.Id == c.DistrictId).Select(d => d.Name).FirstOrDefault(),
                Position = db.Assets.Where(a => a.Id == c.AssetId).Select(a => a.Geom).FirstOrDefault(),
            }).ToListAsync(ct);
        return rows.Select(r => new CardDto(
            r.Card.Id, r.Card.Genus, r.Card.Rarity, r.Card.Frequency, r.Card.Share,
            JsonSerializer.Deserialize<string[]>(r.Card.Reasons) ?? [], r.Card.DistrictId, r.DistrictName, r.Card.AssetId,
            r.Position?.Y ?? 0, r.Position?.X ?? 0, r.Card.CreatedAt)).ToList();
    }

    public async Task<CollectionDto> CollectionAsync(Guid userId, CancellationToken ct)
    {
        // the rarity is stored as text, so the best one is picked in memory
        var cards = await db.Cards.AsNoTracking().Where(c => c.UserId == userId)
            .Select(c => new { c.Genus, c.Rarity, c.CreatedAt }).ToListAsync(ct);

        var byRarity = Enum.GetValues<Rarity>().ToDictionary(
            r => JsonNamingPolicy.SnakeCaseLower.ConvertName(r.ToString()), r => cards.Count(c => c.Rarity == r));
        var genera = cards.GroupBy(c => c.Genus)
            .Select(g => new CollectionEntryDto(g.Key, g.Count(), g.Max(c => c.Rarity), g.Min(c => c.CreatedAt)))
            .OrderByDescending(e => e.BestRarity).ThenBy(e => e.Genus, StringComparer.Ordinal).ToList();
        return new CollectionDto(cards.Count, genera.Count, byRarity, genera);
    }
}
