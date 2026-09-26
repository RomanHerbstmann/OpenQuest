using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Config;
using OpenQuest.Api.Cards;
using OpenQuest.Api.Data;
using OpenQuest.Api.Districts;
using OpenQuest.Api.Eventing;
using OpenQuest.Api.Features;
using OpenQuest.Api.Gamification;
using OpenQuest.Api.Photos;
using OpenQuest.Api.Publishing;
using OpenQuest.Api.Queries;
using OpenQuest.Api.Services;
using OpenQuest.Api.Startup;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Rules;
using OpenQuest.Core.Publishing;

namespace OpenQuest.Api.Composition;

/// <summary>
/// The composition root, one method per module. Consumers depend on the small interfaces; concrete classes are only named here.
/// To add behaviour, register another <see cref="IEventHandler{TEvent}"/>, <see cref="IContributionPublisher"/> or
/// <see cref="IStartupTask"/>; existing code stays untouched.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddOpenQuestOptions(this IServiceCollection s, IConfiguration c)
    {
        s.Configure<AuthOptions>(c.GetSection(AuthOptions.Section));
        s.Configure<JwtOptions>(c.GetSection(JwtOptions.Section));
        s.Configure<AdminOptions>(c.GetSection(AdminOptions.Section));
        s.Configure<GameOptions>(c.GetSection(GameOptions.Section));
        s.Configure<GamificationOptions>(c.GetSection(GamificationOptions.Section));
        s.Configure<StorageOptions>(c.GetSection(StorageOptions.Section));
        s.Configure<OutboxOptions>(c.GetSection(OutboxOptions.Section));
        s.Configure<PublishingOptions>(c.GetSection(PublishingOptions.Section));
        s.AddSingleton(TimeProvider.System);
        return s;
    }

    public static IServiceCollection AddOpenQuestPersistence(this IServiceCollection s, IConfiguration c)
        => s.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(c.GetConnectionString("Default"), npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention());

    public static IServiceCollection AddOpenQuestAuth(this IServiceCollection s, IConfiguration c, IHostEnvironment env)
    {
        var jwt = c.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (jwt.Key.Length < 32)
        {
            // Only in Development: a random key per start (tokens become invalid on restart) instead of a key in git.
            if (!env.IsDevelopment()) throw new InvalidOperationException("Jwt:Key must be set and at least 32 characters long.");
            jwt.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            s.PostConfigure<JwtOptions>(o => o.Key = jwt.Key);
        }

        s.AddSingleton<IPasswordService, PasswordService>();
        s.AddSingleton<ITokenService, TokenService>();
        s.AddScoped<IRecoveryCodeIssuer, RecoveryCodeIssuer>();
        s.AddScoped<IUserRegistration, UserRegistration>();
        s.AddScoped<IUserAuthentication, PasswordAuthentication>();
        s.AddScoped<IAccountRecovery, AccountRecovery>();

        s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = TokenService.SigningKey(jwt),
                ClockSkew = TimeSpan.FromMinutes(1), NameClaimType = "unique_name", RoleClaimType = "role",
            };
        });
        s.AddAuthorizationBuilder()
            .AddPolicy("Admin", p => p.RequireRole("admin"))
            .AddPolicy("Moderator", p => p.RequireRole("moderator", "admin"));

        var perMinute = c.GetValue("Auth:RateLimitPerMinute", 10);
        s.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthEndpoints.RateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
        });
        return s;
    }

    public static IServiceCollection AddOpenQuestStorage(this IServiceCollection s)
    {
        s.AddSingleton<S3BlobStore>();
        s.AddSingleton<IBlobWriter>(sp => sp.GetRequiredService<S3BlobStore>());
        s.AddSingleton<IBlobReader>(sp => sp.GetRequiredService<S3BlobStore>());
        s.AddSingleton<IBlobDeleter>(sp => sp.GetRequiredService<S3BlobStore>());
        s.AddSingleton<IPhotoProcessor, SkiaPhotoProcessor>();
        s.AddScoped<IPhotoDuplicateFinder, DbPhotoDuplicateFinder>();
        s.AddScoped<IPhotoIngestor, PhotoIngestor>();
        return s;
    }

    public static IServiceCollection AddOpenQuestGame(this IServiceCollection s)
    {
        s.AddSingleton<IJsonSchemaValidator, JsonSchemaValidator>();
        s.AddSingleton<IAttributeChangeFactory, AttributeChangeFactory>();
        s.AddScoped<IQuestSlotLedger, QuestSlotLedger>();
        s.AddScoped<IQuestClaimService, QuestClaimService>();
        s.AddScoped<IClaimExpiryService, ClaimExpiryService>();
        s.AddScoped<ISubmissionService, SubmissionService>();
        s.AddScoped<ISubmissionReviewService, SubmissionReviewService>();
        s.AddScoped<IQuestCampaignService, QuestCampaignService>();

        s.AddScoped<INearbyQuests, NearbyQuests>();
        s.AddScoped<INearbyAssets, NearbyAssets>();
        s.AddScoped<IPlayerClaims, PlayerClaims>();
        s.AddScoped<IModerationQueue, ModerationQueue>();
        s.AddScoped<IQuestOverview, QuestOverview>();
        s.AddScoped<IPublicationOverview, PublicationOverview>();
        s.AddScoped<IAssetHistory, AssetHistory>();
        s.AddScoped<IImportedFeeds, ImportedFeeds>();

        s.AddHostedService<ClaimExpiryWorker>();
        return s;
    }

    /// <summary>Points, levels and (later) leaderboards, cards and recurring quests. The rules live in the core.</summary>
    public static IServiceCollection AddOpenQuestGamification(this IServiceCollection s)
    {
        s.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GamificationOptions>>().Value;
            var levels = o.LevelThresholds.Length > 0 ? new LevelCurve(o.LevelThresholds) : LevelCurve.Default;
            return new GamificationProfile(levels, o.Rarity.ToProfile());
        });
        s.AddScoped<IPlayerProgress, PlayerProgress>();

        // cities and districts (drawn by admins), the leaderboard and the lookup of a tree's district
        s.AddScoped<IDistrictShapeChecker, DistrictShapeChecker>();
        s.AddScoped<ICityAdmin, CityAdmin>();
        s.AddScoped<IDistrictAdmin, DistrictAdmin>();
        s.AddScoped<IDistrictDirectory, DistrictDirectory>();
        s.AddScoped<IDistrictLocator, DistrictLocator>();
        s.AddScoped<ILeaderboards, Leaderboards>();

        // cards: the rarity of a card depends on how frequent the genus is in the district
        s.AddScoped<IDistrictGenusStats, DistrictGenusStats>();
        s.AddScoped<ICardCollection, CardCollection>();
        return s;
    }

    /// <summary>Domain events with a transactional outbox: see <see cref="OutboxProcessor"/>.</summary>
    public static IServiceCollection AddOpenQuestEventing(this IServiceCollection s)
    {
        s.AddSingleton<IOutboxClock, SystemOutboxClock>();
        s.AddSingleton<EventTypeRegistry>();
        s.AddSingleton<IEventDispatcher, EventDispatcher>();
        s.AddScoped<IEventPublisher, OutboxEventPublisher>();
        s.AddHostedService<OutboxProcessor>();

        // Handlers: add one line per reaction to an event.
        s.AddScoped<IEventHandler<AttributeChangeAccepted>, PublishAcceptedChangesHandler>();
        s.AddScoped<IEventHandler<SubmissionApproved>, AwardPointsHandler>();
        s.AddScoped<IEventHandler<SubmissionApproved>, AwardCardHandler>();
        return s;
    }

    public static IServiceCollection AddOpenQuestPublishing(this IServiceCollection s, IConfiguration c)
    {
        s.AddSingleton<IContributionPublisher, StorageContributionPublisher>();
        if (!string.IsNullOrWhiteSpace(c[$"{PublishingOptions.Section}:GitHub:Token"]))
        {
            s.AddHttpClient<GitHubContributionPublisher>(h => h.DefaultRequestHeaders.UserAgent.ParseAdd("OpenQuest/0.1"));
            s.AddTransient<IContributionPublisher>(sp => sp.GetRequiredService<GitHubContributionPublisher>());
        }
        s.AddSingleton<IPublishedFeed, BlobPublishedFeed>();
        s.AddScoped<IChangeRepublisher, ChangeRepublisher>();
        return s;
    }

    public static IServiceCollection AddOpenQuestStartupTasks(this IServiceCollection s)
    {
        // Order matters: migrate, then seed the catalog, then the admin.
        foreach (var type in new[] { typeof(MigrateDatabaseTask), typeof(SeedCatalogTask), typeof(SeedAdminTask) })
        {
            s.AddScoped(type);
            s.AddSingleton(new StartupTaskDescriptor(type));
        }
        s.AddSingleton<StartupTaskRunner>();
        return s;
    }

    public static IEndpointRouteBuilder MapOpenQuestApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("System");
        app.MapAuth();
        app.MapPlayer();
        app.MapGamification();
        app.MapCities();
        app.MapCards();
        app.MapDistrictAdmin();
        app.MapMedia();
        app.MapAdminQuests();
        app.MapModeration();
        app.MapPublishing();
        app.MapSync();
        return app;
    }
}
