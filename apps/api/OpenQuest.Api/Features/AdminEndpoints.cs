using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Queries;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Features;

public static class AdminEndpoints
{
    public static void MapAdminQuests(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").RequireAuthorization("Admin").WithTags("Admin");

        g.MapPost("/quests", async (CreateQuestsRequest req, ClaimsPrincipal user, IQuestCampaignService campaigns, CancellationToken ct) =>
            (await campaigns.CreateAsync(user.GetUserId(), req, ct)).ToHttp(Results.Ok))
            .WithName("CreateQuests")
            .WithSummary("Creates a campaign with one quest per selected asset (single asset, map selection, or attribute filter).");

        g.MapGet("/quests", async (string? status, int? limit, int? offset, IQuestOverview overview, CancellationToken ct) =>
        {
            QuestStatus? parsed = null;
            if (status is not null)
            {
                if (!EnumParsing.TryParseSnake<QuestStatus>(status, out var qs))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["draft, active, paused, full or closed"] });
                parsed = qs;
            }
            return Results.Ok(await overview.ListQuestsAsync(parsed, offset ?? 0, Math.Clamp(limit ?? 50, 1, 200), ct));
        }).WithName("AdminListQuests");

        g.MapGet("/campaigns", async (int? limit, IQuestOverview overview, CancellationToken ct) =>
            Results.Ok(await overview.ListCampaignsAsync(Math.Clamp(limit ?? 50, 1, 200), ct)))
            .WithName("AdminListCampaigns");

        g.MapPost("/quests/{id:guid}/status", async (Guid id, SetQuestStatusRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (req.Status == QuestStatus.Full)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["'full' is set automatically."] });
            var quest = await db.Quests.FirstOrDefaultAsync(q => q.Id == id, ct);
            if (quest is null) return Results.NotFound();
            quest.Status = QuestSlots.StatusAfterSlotChange(req.Status, quest.MaxCompletions, quest.SlotsTaken);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { quest.Id, status = quest.Status });
        }).WithName("SetQuestStatus").WithSummary("Pause, resume, close or activate a quest.");
    }
}
