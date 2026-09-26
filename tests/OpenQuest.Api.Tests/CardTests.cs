using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Tests;

/// <summary>Tree cards: what is on them, how rare they are and how the tree book adds up.</summary>
[Collection(ApiCollection.Name)]
public class CardTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly GeometryFactory Wgs84 = NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    private sealed record Tree(Guid Id, double Lat, double Lon);

    /// <summary>A city with one big district and 1000 trees with known genus in it: Tilia 974, Acer 20, Ginkgo 5, Metasequoia 1 (plus 10 trees without genus).</summary>
    private sealed class Setup
    {
        public HttpClient Admin = null!;
        public Guid CityId, DistrictId;
        public double Lat0, Lon0;
        public Dictionary<string, List<Tree>> Trees = new();
    }

    private async Task<Setup> ArrangeAsync(double lat0, int tilia = 974, int acer = 20, int ginkgo = 5, int meta = 1, int withoutGenus = 10)
    {
        var s = new Setup { Admin = await api.AdminAsync(), Lat0 = lat0, Lon0 = 7.0 };
        var city = await s.Admin.PostAsJsonAsync("/admin/cities", new { name = "Kartenstadt " + Guid.NewGuid().ToString("N")[..8] });
        s.CityId = (await city.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var ring = new JsonArray(new JsonArray(s.Lon0, lat0), new JsonArray(s.Lon0 + 0.1, lat0), new JsonArray(s.Lon0 + 0.1, lat0 + 0.1), new JsonArray(s.Lon0, lat0 + 0.1), new JsonArray(s.Lon0, lat0));
        var geometry = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) };
        var district = await s.Admin.PostAsJsonAsync($"/admin/cities/{s.CityId}/districts", new { name = "Grosses Viertel", geometry }, Web);
        Assert.Equal(HttpStatusCode.Created, district.StatusCode);
        s.DistrictId = (await district.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var counts = new (string? Genus, int Count)[] { ("Tilia", tilia), ("Acer", acer), ("Ginkgo", ginkgo), ("Metasequoia", meta), (null, withoutGenus) };
        await api.WithDb(async db =>
        {
            var type = await db.AssetTypes.FirstAsync(t => t.Key == "tree");
            var source = await db.DataSources.FirstAsync();
            var now = api.Clock.GetUtcNow();
            var i = 0;
            foreach (var (genus, count) in counts)
            {
                var list = new List<Tree>();
                for (var n = 0; n < count; n++, i++)
                {
                    var (lat, lon) = (lat0 + 0.001 + (i % 30) * 0.0025, s.Lon0 + 0.001 + (i / 30) * 0.0025);
                    var id = Guid.NewGuid();
                    db.Assets.Add(new AssetEntity
                    {
                        Id = id, AssetTypeId = type.Id, DataSourceId = source.Id, ExternalId = id.ToString("N"), Geom = Wgs84.CreatePoint(new Coordinate(lon, lat)),
                        Attributes = genus is null ? """{"genus":null,"quality_flags":["placeholder_genus"]}""" : $$"""{"genus":"{{genus}}","quality_flags":[]}""",
                        Raw = "{}", SourceHash = "x", FirstSeenAt = now, LastSeenAt = now, UpdatedAt = now,
                    });
                    list.Add(new Tree(id, lat, lon));
                }
                s.Trees[genus ?? "none"] = list;
            }
            await db.SaveChangesAsync();
            return 0;
        });
        return s;
    }

    private async Task<Guid> ApproveAsync(HttpClient admin, HttpClient player, Tree tree, string taskType, object payload, object? taskConfig = null)
    {
        var questId = await api.CreateQuestAsync(admin, tree.Id, taskType, taskConfig: taskConfig);
        var claim = await (await player.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<JsonElement>();
        var form = new MultipartFormDataContent
        {
            { new StringContent(tree.Lat.ToString(CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(tree.Lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(payload)), "payload" },
        };
        var submitted = await player.PostAsync($"/claims/{claim.GetProperty("id").GetGuid()}/submit", form);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var submissionId = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        return submissionId;
    }

    private Task<Guid> ConfirmGenusAsync(Setup s, HttpClient player, Tree tree, string value)
        => ApproveAsync(s.Admin, player, tree, "verify_attribute", new { value });

    /// <summary>Waits until the event of the submission was delivered to all handlers, then returns its card (or null).</summary>
    private async Task<Card?> CardOfAsync(Guid submissionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var delivered = await api.WithDb(db => db.OutboxMessages.AnyAsync(m =>
                m.Type == nameof(SubmissionApproved) && m.Status == OutboxStatus.Processed && EF.Functions.JsonContains(m.Payload, "{\"submissionId\":\"" + submissionId + "\"}")));
            if (delivered) return await api.WithDb(db => db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.SubmissionId == submissionId));
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The approval was not delivered within 20 s.");
    }

    private static string[] Reasons(Card c) => JsonSerializer.Deserialize<string[]>(c.Reasons)!;

    // ---- what is on a card -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Confirming_the_genus_of_an_abundant_tree_gives_a_card_of_that_genus_with_the_districts_numbers()
    {
        var s = await ArrangeAsync(10.0);
        var (player, _, _) = await api.RegisterAsync("collector");
        var submission = await ConfirmGenusAsync(s, player, s.Trees["Tilia"][0], "tilia");   // spelled loosely, confirms the known genus

        var card = await CardOfAsync(submission);
        Assert.NotNull(card);
        Assert.Equal("Tilia", card.Genus);
        Assert.Equal(Frequency.Abundant, card.Frequency);
        Assert.Equal(0.974, card.Share!.Value, 6);
        Assert.Equal(s.DistrictId, card.DistrictId);
        Assert.Empty(Reasons(card));   // nothing special: confirmed data, common tree
        Assert.Equal(s.Trees["Tilia"][0].Id, card.AssetId);
        // an ordinary tree never gives a legendary card, and the dice roll depends only on the submission
        Assert.NotEqual(Rarity.Legendary, card.Rarity);
        Assert.Equal(card.Rarity, CardRarity.Decide(new RarityInput(card.Share, 1000, false, false), CardRarity.RollFor(submission), RarityProfile.Default).Rarity);
    }

    [Fact]
    public async Task A_scarce_genus_is_classified_scarce_and_named_in_the_reasons()
    {
        var s = await ArrangeAsync(11.0);
        var (player, _, _) = await api.RegisterAsync("gingko");
        var submission = await ConfirmGenusAsync(s, player, s.Trees["Ginkgo"][0], "Ginkgo");

        var card = (await CardOfAsync(submission))!;
        Assert.Equal((Frequency.Scarce, "Ginkgo"), (card.Frequency, card.Genus));
        Assert.Equal(0.005, card.Share!.Value, 6);
        Assert.Equal(["scarce_in_district"], Reasons(card));
        Assert.Equal(card.Rarity, CardRarity.Decide(new RarityInput(card.Share, 1000, false, false), CardRarity.RollFor(submission), RarityProfile.Default).Rarity);
    }

    [Fact]
    public async Task Finding_out_the_genus_of_a_tree_without_one_is_new_information()
    {
        var s = await ArrangeAsync(12.0);
        var (player, _, _) = await api.RegisterAsync("detective");

        // Metasequoia: one tree in the district (0.1 %)
        var known = await CardOfAsync(await ConfirmGenusAsync(s, player, s.Trees["none"][0], "Metasequoia glyptostroboides"));
        Assert.Equal("Metasequoia", known!.Genus);   // the card is per genus
        Assert.Equal(Frequency.VeryScarce, known.Frequency);
        Assert.Equal(["scarce_in_district", "new_information"], Reasons(known));

        // a genus that does not grow in the district at all
        var unknown = await CardOfAsync(await ConfirmGenusAsync(s, player, s.Trees["none"][1], "Sequoiadendron"));
        Assert.Equal((Frequency.VeryScarce, 0.0), (unknown!.Frequency, unknown.Share));
        Assert.Equal(["new_to_district", "new_information"], Reasons(unknown));

        // and a common one: only the boost from the new information shows in the chances, not in the class
        var common = await CardOfAsync(await ConfirmGenusAsync(s, player, s.Trees["none"][2], "Tilia"));
        Assert.Equal(Frequency.Abundant, common!.Frequency);
        Assert.Equal(["new_information"], Reasons(common));
    }

    [Fact]
    public async Task Correcting_the_genus_counts_as_new_information_and_the_card_shows_the_corrected_genus()
    {
        var s = await ArrangeAsync(13.0);
        var (player, _, _) = await api.RegisterAsync("corrector");
        var card = (await CardOfAsync(await ConfirmGenusAsync(s, player, s.Trees["Tilia"][1], "Acer")))!;
        Assert.Equal("Acer", card.Genus);
        Assert.Equal(Frequency.Common, card.Frequency);   // 2 % of the district
        Assert.Equal(["new_information"], Reasons(card));
    }

    [Fact]
    public async Task A_reported_problem_with_the_tree_is_a_fact_that_improves_the_chances()
    {
        var s = await ArrangeAsync(14.0);
        var (player, _, _) = await api.RegisterAsync("inspector");

        var dead = (await CardOfAsync(await ApproveAsync(s.Admin, player, s.Trees["Tilia"][2], "condition_report", new { condition = "dead" })))!;
        Assert.Equal(("Tilia", Frequency.Abundant), (dead.Genus, dead.Frequency));
        Assert.Equal(["condition_fact"], Reasons(dead));

        var fine = (await CardOfAsync(await ApproveAsync(s.Admin, player, s.Trees["Tilia"][3], "condition_report", new { condition = "good" })))!;
        Assert.Empty(Reasons(fine));   // a healthy tree is no news
    }

    [Fact]
    public async Task No_genus_known_and_none_found_out_gives_no_card()
    {
        var s = await ArrangeAsync(15.0);
        var (player, _, _) = await api.RegisterAsync("unlucky");
        var submission = await ApproveAsync(s.Admin, player, s.Trees["none"][3], "condition_report", new { condition = "damaged" });
        Assert.Null(await CardOfAsync(submission));
        Assert.Equal(0, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("cardCount").GetInt32());
    }

    [Fact]
    public async Task A_tree_outside_every_district_still_gives_a_card_with_ordinary_chances()
    {
        var s = await ArrangeAsync(16.0);
        var (player, _, _) = await api.RegisterAsync("wanderer");
        var lonely = new Tree(await api.AddTreeAsync(30.0, 30.0, "Fagus"), 30.0, 30.0);   // far away from the district
        var card = (await CardOfAsync(await ConfirmGenusAsync(s, player, lonely, "Fagus")))!;
        Assert.Equal(("Fagus", null, Frequency.Common), (card.Genus, card.DistrictId, card.Frequency));
        Assert.Null(card.Share);
    }

    [Fact]
    public async Task A_redelivered_approval_does_not_hand_out_a_second_card()
    {
        var s = await ArrangeAsync(17.0);
        var (player, username, _) = await api.RegisterAsync("twice");
        var submission = await ConfirmGenusAsync(s, player, s.Trees["Tilia"][4], "Tilia");
        var first = (await CardOfAsync(submission))!;
        var userId = await api.WithDb(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).FirstAsync());
        var questId = await api.WithDb(db => db.Submissions.Where(x => x.Id == submission).Select(x => x.Claim.QuestId).FirstAsync());

        using var scope = api.Services.CreateScope();
        var again = new SubmissionApproved(submission, userId, questId, 10);
        foreach (var handler in scope.ServiceProvider.GetServices<IEventHandler<SubmissionApproved>>())
        {
            await handler.HandleAsync([again], CancellationToken.None);
            await handler.HandleAsync([again, again], CancellationToken.None);
        }

        var cards = await api.WithDb(db => db.Cards.Where(c => c.SubmissionId == submission).ToListAsync());
        var card = Assert.Single(cards);
        Assert.Equal((first.Id, first.Rarity), (card.Id, card.Rarity));
    }

    // ---- the tree book -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_tree_book_lists_cards_and_adds_them_up_per_genus()
    {
        var s = await ArrangeAsync(18.0);
        var (player, _, _) = await api.RegisterAsync("bookworm");
        var (other, _, _) = await api.RegisterAsync("bystander");

        var s1 = await ConfirmGenusAsync(s, player, s.Trees["Tilia"][5], "Tilia");
        var s2 = await ConfirmGenusAsync(s, player, s.Trees["Tilia"][6], "Tilia");
        var s3 = await ConfirmGenusAsync(s, player, s.Trees["Ginkgo"][1], "Ginkgo");
        var s4 = await ConfirmGenusAsync(s, player, s.Trees["Acer"][0], "Acer");
        var cards = new List<Card>();
        foreach (var s0 in new[] { s1, s2, s3, s4 }) cards.Add((await CardOfAsync(s0))!);

        var list = (await player.GetFromJsonAsync<List<JsonElement>>("/me/cards"))!;
        Assert.Equal(4, list.Count);
        Assert.Equal(cards.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => c.Id), list.Select(c => c.GetProperty("id").GetGuid()));
        var ginkgo = list.Single(c => c.GetProperty("genus").GetString() == "Ginkgo");
        Assert.Equal("scarce", ginkgo.GetProperty("frequency").GetString());
        Assert.Equal("Grosses Viertel", ginkgo.GetProperty("districtName").GetString());
        Assert.Equal(s.Trees["Ginkgo"][1].Lat, ginkgo.GetProperty("lat").GetDouble(), 6);
        Assert.Equal(["scarce_in_district"], ginkgo.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()!));
        Assert.Contains(ginkgo.GetProperty("rarity").GetString(), new[] { "common", "uncommon", "rare", "legendary" });
        Assert.Single((await player.GetFromJsonAsync<List<JsonElement>>("/me/cards?limit=1"))!);
        Assert.Equal(3, (await player.GetFromJsonAsync<List<JsonElement>>("/me/cards?offset=1&limit=50"))!.Count);

        var book = await player.GetFromJsonAsync<JsonElement>("/me/collection");
        Assert.Equal((4, 3), (book.GetProperty("totalCards").GetInt32(), book.GetProperty("distinctGenera").GetInt32()));
        var byRarity = book.GetProperty("byRarity");
        Assert.Equal(4, byRarity.EnumerateObject().Count());   // all four are there, also with 0
        Assert.Equal(4, byRarity.EnumerateObject().Sum(p => p.Value.GetInt32()));
        var tilia = book.GetProperty("genera").EnumerateArray().Single(g => g.GetProperty("genus").GetString() == "Tilia");
        Assert.Equal(2, tilia.GetProperty("count").GetInt32());
        var best = cards.Where(c => c.Genus == "Tilia").Max(c => c.Rarity);
        Assert.Equal(best.ToString().ToLowerInvariant(), tilia.GetProperty("bestRarity").GetString());
        Assert.Equal(4, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("cardCount").GetInt32());

        // nobody else sees these cards
        Assert.Empty((await other.GetFromJsonAsync<List<JsonElement>>("/me/cards"))!);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/me/collection")).GetProperty("totalCards").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me/cards")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me/collection")).StatusCode);
    }

    // ---- genus statistics of a district ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_genus_statistics_of_a_district_are_listed_and_recalculated_on_demand()
    {
        var s = await ArrangeAsync(19.0);
        var (player, _, _) = await api.RegisterAsync("statsreader");

        var stats = await player.GetFromJsonAsync<JsonElement>($"/districts/{s.DistrictId}/genera");
        Assert.Equal(1000, stats.GetProperty("knownGenusTrees").GetInt32());   // the 10 trees without genus are not counted
        Assert.Equal(4, stats.GetProperty("genusCount").GetInt32());
        var genera = stats.GetProperty("genera").EnumerateArray().ToList();
        Assert.Equal(new[] { "Tilia", "Acer", "Ginkgo", "Metasequoia" }, genera.Select(g => g.GetProperty("genus").GetString()!));
        Assert.Equal(new[] { 974, 20, 5, 1 }, genera.Select(g => g.GetProperty("treeCount").GetInt32()));
        Assert.Equal(new[] { "abundant", "common", "scarce", "very_scarce" }, genera.Select(g => g.GetProperty("frequency").GetString()!));
        Assert.Equal(0.974, genera[0].GetProperty("share").GetDouble(), 6);
        Assert.Single((await player.GetFromJsonAsync<JsonElement>($"/districts/{s.DistrictId}/genera?limit=1")).GetProperty("genera").EnumerateArray());

        // 2000 more Ginkgo: the numbers only change when they are recalculated (players and admins), and an admin can do that now
        await api.WithDb(async db =>
        {
            var type = await db.AssetTypes.FirstAsync(t => t.Key == "tree");
            var source = await db.DataSources.FirstAsync();
            var now = api.Clock.GetUtcNow();
            for (var i = 0; i < 2000; i++)
                db.Assets.Add(new AssetEntity
                {
                    AssetTypeId = type.Id, DataSourceId = source.Id, ExternalId = Guid.NewGuid().ToString("N"),
                    Geom = Wgs84.CreatePoint(new Coordinate(s.Lon0 + 0.09 + (i % 40) * 0.0002, s.Lat0 + 0.09 + (i / 40) * 0.0001)),
                    Attributes = """{"genus":"Ginkgo","quality_flags":[]}""", Raw = "{}", SourceHash = "x", FirstSeenAt = now, LastSeenAt = now, UpdatedAt = now,
                });
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsync($"/admin/districts/{s.DistrictId}/genera/refresh", null)).StatusCode);
        var refreshed = await s.Admin.PostAsync($"/admin/districts/{s.DistrictId}/genera/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(4, (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("genera").GetInt32());

        var after = await player.GetFromJsonAsync<JsonElement>($"/districts/{s.DistrictId}/genera");
        Assert.Equal(3000, after.GetProperty("knownGenusTrees").GetInt32());
        Assert.Equal("Ginkgo", after.GetProperty("genera")[0].GetProperty("genus").GetString());
        Assert.Equal(2005, after.GetProperty("genera")[0].GetProperty("treeCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.PostAsync($"/admin/districts/{Guid.NewGuid()}/genera/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/districts/{Guid.NewGuid()}/genera")).StatusCode);
    }

    [Fact]
    public async Task Redrawing_a_district_recalculates_its_statistics_and_a_deactivated_one_is_hidden()
    {
        var s = await ArrangeAsync(20.0, tilia: 100, acer: 0, ginkgo: 0, meta: 0, withoutGenus: 0);
        var (player, _, _) = await api.RegisterAsync("redrawer");
        var before = await player.GetFromJsonAsync<JsonElement>($"/districts/{s.DistrictId}/genera");
        Assert.Equal(100, before.GetProperty("knownGenusTrees").GetInt32());

        // the district shrinks to a corner without trees: after redrawing the statistics follow (the redraw touches updated_at)
        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var corner = new JsonObject
        {
            ["type"] = "Polygon",
            ["coordinates"] = new JsonArray(new JsonArray(new JsonArray(s.Lon0 + 0.095, s.Lat0 + 0.095), new JsonArray(s.Lon0 + 0.099, s.Lat0 + 0.095),
                new JsonArray(s.Lon0 + 0.099, s.Lat0 + 0.099), new JsonArray(s.Lon0 + 0.095, s.Lat0 + 0.099), new JsonArray(s.Lon0 + 0.095, s.Lat0 + 0.095))),
        };
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PutAsJsonAsync($"/admin/districts/{s.DistrictId}/geometry", new { geometry = corner }, Web)).StatusCode);
        var after = await player.GetFromJsonAsync<JsonElement>($"/districts/{s.DistrictId}/genera");
        Assert.Equal(0, after.GetProperty("knownGenusTrees").GetInt32());

        await s.Admin.PutAsJsonAsync($"/admin/districts/{s.DistrictId}", new { isActive = false }, Web);
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/districts/{s.DistrictId}/genera")).StatusCode);
    }

    [Fact]
    public async Task A_district_with_hardly_any_trees_makes_nothing_scarce()
    {
        var s = await ArrangeAsync(21.0, tilia: 8, acer: 1, ginkgo: 0, meta: 0, withoutGenus: 0);   // 9 known trees: too few to call anything scarce
        var (player, _, _) = await api.RegisterAsync("smallville");
        var card = (await CardOfAsync(await ConfirmGenusAsync(s, player, s.Trees["Acer"][0], "Acer")))!;
        Assert.Equal(Frequency.Common, card.Frequency);
        Assert.Empty(Reasons(card));
    }
}
