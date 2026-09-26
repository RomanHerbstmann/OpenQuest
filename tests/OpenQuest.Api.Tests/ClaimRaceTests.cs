using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Tests;

[Collection(ApiCollection.Name)]
public class ClaimRaceTests(ApiFactory api)
{
    [Fact]
    public async Task Fifty_parallel_claims_on_a_quest_with_three_slots_grant_exactly_three()
    {
        var admin = await api.AdminAsync();
        var assetId = await api.AddTreeAsync(51.96, 7.62);
        var questId = await api.CreateQuestAsync(admin, assetId, maxCompletions: 3);

        var players = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => api.RegisterAsync("racer")));
        var results = await Task.WhenAll(players.Select(p => p.Client.PostAsync($"/quests/{questId}/claim", null)));

        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(47, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var (slots, status, activeClaims) = await api.WithDb(async db =>
        {
            var q = await db.Quests.AsNoTracking().FirstAsync(x => x.Id == questId);
            return (q.SlotsTaken, q.Status, await db.Claims.CountAsync(c => c.QuestId == questId && c.Status == ClaimStatus.Active));
        });
        Assert.Equal(3, slots);
        Assert.Equal(QuestStatus.Full, status);
        Assert.Equal(3, activeClaims);
    }

    [Fact]
    public async Task Expired_claims_release_their_slot()
    {
        var admin = await api.AdminAsync();
        var assetId = await api.AddTreeAsync(51.961, 7.621);
        var questId = await api.CreateQuestAsync(admin, assetId, maxCompletions: 1);

        var a = await api.RegisterAsync("first");
        var b = await api.RegisterAsync("second");
        Assert.Equal(HttpStatusCode.Created, (await a.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode);

        api.Clock.Advance(TimeSpan.FromMinutes(31)); // default claim TTL is 30 min

        // Even before the background job sweeps, the next claim releases the stale one.
        Assert.Equal(HttpStatusCode.Created, (await b.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode);
        var mine = await a.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/me/claims");
        Assert.Equal("expired", mine[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelling_a_claim_frees_the_slot()
    {
        var admin = await api.AdminAsync();
        var questId = await api.CreateQuestAsync(admin, await api.AddTreeAsync(51.962, 7.622));
        var a = await api.RegisterAsync("canceller");
        var b = await api.RegisterAsync("waiting");

        var claim = await (await a.Client.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, (await b.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync($"/claims/{claim.GetProperty("id").GetGuid()}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode);
    }
}
