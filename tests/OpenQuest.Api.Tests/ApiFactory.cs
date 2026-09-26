using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Data;
using OpenQuest.Api.Publishing;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Publishing;
using OpenQuest.Core.Review;
using OpenQuest.Core.Domain;
using Testcontainers.PostgreSql;

namespace OpenQuest.Api.Tests;

/// <summary>One API + database shared by all test classes (the API reads process-wide environment variables at start-up).</summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>
/// Boots the real API against a throw-away PostGIS database, with in-memory photo storage and a fake clock.
/// By default a Testcontainers PostGIS container is started. If OPENQUEST_TEST_DB is set (admin connection string of a
/// running PostGIS server, e.g. the one from docker-compose), a fresh database is created there instead and dropped afterwards.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "admin-test-password";

    private static readonly string? ExternalServer = Environment.GetEnvironmentVariable("OPENQUEST_TEST_DB");
    private readonly PostgreSqlContainer? _container = ExternalServer is null
        ? new PostgreSqlBuilder("postgis/postgis:17-3.5").WithDatabase("openquest").WithUsername("test").WithPassword("test").Build()
        : null;
    private string _databaseName = "";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    public InMemoryBlobStore Storage { get; } = new();
    public ScriptedPublisher Publisher { get; } = new();
    /// <summary>Publishing to open data is off by default (ADR-0014); the tests that look at the feed run with it on, the ones about "off" switch it.</summary>
    public SwitchablePublishingGate PublishingGate { get; } = new() { Enabled = true };
    public ScriptedAutoReviewer AutoReviewer { get; } = new();

    public async Task InitializeAsync()
    {
        string connectionString;
        if (_container is not null)
        {
            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }
        else
        {
            _databaseName = "oq_test_" + Guid.NewGuid().ToString("N")[..12];
            await using var admin = new Npgsql.NpgsqlConnection(ExternalServer);
            await admin.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await cmd.ExecuteNonQueryAsync();
            connectionString = new Npgsql.NpgsqlConnectionStringBuilder(ExternalServer) { Database = _databaseName }.ConnectionString;
        }
        // Program reads configuration while building, so use environment variables (picked up by CreateBuilder).
        Set("ConnectionStrings__Default", connectionString);
        Set("Jwt__Key", "test-key-test-key-test-key-test-key-1234");
        Set("Admin__Username", AdminUser);
        Set("Admin__Password", AdminPassword);
        Set("Auth__Argon2MemoryKiB", "1024"); // fast hashing in tests
        Set("Auth__Argon2TimeCost", "1");
        Set("Auth__RateLimitPerMinute", "100000");
        Set("Outbox__RetryBaseDelayMs", "200");
        Set("AutoReview__Enabled", "true");   // the checker itself is scripted (see ConfigureWebHost); by default it only says "review"
        Set("AutoReview__VerifyUrl", "http://localhost:1/api/verify");
        Set("Gamification__QuestScheduleIntervalSeconds", "86400"); // the tests start schedule runs themselves, the worker only ticks once at start
        Set("Cors__Origins", "http://localhost:3000,https://localhost,capacitor://localhost"); // web frontend and the phone app
        Set("Storage__AccessKey", "x");
        Set("Storage__SecretKey", "x");
        _ = Services; // start the host (runs migrations + seeding)
        await AddDataSourceAsync();
    }

    /// <summary>The importer creates data sources in production; the tests need one to hang assets on.</summary>
    private Task AddDataSourceAsync() => WithDb(async db =>
    {
        db.DataSources.Add(new DataSource
        {
            Key = "de-muenster-trees", AdapterKey = "de_muenster.trees", Name = "Trees (test)", City = "Testcity",
            SourceUrl = "https://example.org", License = "dl-de/by-2.0",
            Attribution = "Datenquelle: Stadt Münster, Digitales Baumkataster, dl-de/by-2-0 (Testdaten)",
        });
        await db.SaveChangesAsync();
        return 0;
    });

    private static void Set(string key, string value) => Environment.SetEnvironmentVariable(key, value);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            s.RemoveAll<S3BlobStore>();
            s.RemoveAll<IBlobWriter>();
            s.RemoveAll<IBlobReader>();
            s.RemoveAll<IBlobDeleter>();
            s.AddSingleton<IBlobWriter>(Storage);
            s.AddSingleton<IBlobReader>(Storage);
            s.AddSingleton<IBlobDeleter>(Storage);
            s.AddSingleton<IContributionPublisher>(Publisher);
            s.RemoveAll<IPublishingGate>();
            s.AddSingleton<IPublishingGate>(PublishingGate);
            s.RemoveAll<ISubmissionAutoReviewer>();
            s.AddSingleton<ISubmissionAutoReviewer>(AutoReviewer);
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        else
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            await using var admin = new Npgsql.NpgsqlConnection(ExternalServer);
            await admin.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    public async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Inserts a tree asset at the given position (as the importer would) and returns its id.</summary>
    public Task<Guid> AddTreeAsync(double lat, double lon, string? genus = null) => WithDb(async db =>
    {
        var type = await db.AssetTypes.FirstAsync(t => t.Key == "tree");
        var source = await db.DataSources.FirstAsync();
        var now = Clock.GetUtcNow();
        var geo = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var asset = new AssetEntity
        {
            AssetTypeId = type.Id, DataSourceId = source.Id, ExternalId = Guid.NewGuid().ToString("N")[..20],
            Geom = geo.CreatePoint(new Coordinate(lon, lat)),
            Attributes = $$"""{"genus":{{(genus is null ? "null" : $"\"{genus}\"")}},"quality_flags":[]}""",
            Raw = "{}", SourceHash = "x", FirstSeenAt = now, LastSeenAt = now, UpdatedAt = now,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    });

    public async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = CreateClient();
        var r = await client.PostAsJsonAsync("/auth/login", new { username, password });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    public Task<HttpClient> AdminAsync() => LoginAsync(AdminUser, AdminPassword);

    public async Task<(HttpClient Client, string Username, string[] RecoveryCodes)> RegisterAsync(string? name = null)
    {
        var username = (name ?? "user") + Guid.NewGuid().ToString("N")[..8];
        var client = CreateClient();
        var r = await client.PostAsJsonAsync("/auth/register", new { username, password = "correcthorse" });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        var codes = body.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray();
        return (client, username, codes);
    }

    /// <summary>Creates a quest for one asset via the admin API and returns the quest id.</summary>
    public async Task<Guid> CreateQuestAsync(HttpClient admin, Guid assetId, string taskType = "verify_attribute",
        int maxCompletions = 1, object? taskConfig = null, int rewardPoints = 10)
    {
        var r = await admin.PostAsJsonAsync("/admin/quests", new
        {
            taskType, maxCompletions, rewardPoints,
            taskConfig = taskConfig ?? (taskType is "verify_attribute" or "measure" ? new { attribute = "genus" } : null),
            target = new { assetIds = new[] { assetId } },
        });
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, text);
        var json = System.Text.Json.JsonDocument.Parse(text).RootElement;
        return json.GetProperty("questIds")[0].GetGuid();
    }
}

public sealed class InMemoryBlobStore : IBlobWriter, IBlobReader, IBlobDeleter
{
    public Dictionary<string, byte[]> Objects { get; } = new();
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    { lock (Objects) Objects[key] = content; return Task.CompletedTask; }
    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    { lock (Objects) return Task.FromResult(Objects.GetValueOrDefault(key)); }
    public Task DeleteAsync(string key, CancellationToken ct = default)
    { lock (Objects) Objects.Remove(key); return Task.CompletedTask; }
}

/// <summary>An extra publisher next to the real ones that records batches and can be told to fail, to test retries.</summary>
public sealed class ScriptedPublisher : IContributionPublisher
{
    private int _failNext;
    public string Name => "scripted";
    public List<PublishBatch> Batches { get; } = [];
    public int Attempts;

    public void FailNext(int times) => Interlocked.Exchange(ref _failNext, times);

    public Task<PublishResult> PublishAsync(PublishBatch batch, CancellationToken ct)
    {
        Interlocked.Increment(ref Attempts);
        if (Interlocked.Decrement(ref _failNext) >= 0) throw new InvalidOperationException("scripted failure");
        Interlocked.Exchange(ref _failNext, 0);
        lock (Batches) Batches.Add(batch);
        return Task.FromResult(new PublishResult("scripted://ok"));
    }
}

/// <summary>
/// Stands in for the photo verification. Without a script it answers "review" (a moderator decides), so tests that do not care are not affected.
/// A test scripts the answer for its own tree by the tree's latitude (every test works on its own patch of the map).
/// </summary>
public sealed class ScriptedAutoReviewer : ISubmissionAutoReviewer
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<double, Func<int, AutoReviewDecision>> _scripts = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<double, int> _attempts = new();
    public System.Collections.Concurrent.ConcurrentQueue<AutoReviewRequest> Calls { get; } = new();

    private static double Key(double lat) => Math.Round(lat, 4);

    /// <summary>The answer for photos of the tree at this latitude; the function gets the attempt number (1, 2, ...) and may throw.</summary>
    public void On(double expectedLat, Func<int, AutoReviewDecision> answer) => _scripts[Key(expectedLat)] = answer;
    public void On(double expectedLat, AutoReviewVerdict verdict, params string[] reasons)
        => On(expectedLat, _ => new AutoReviewDecision(verdict, reasons, "{\"scripted\":true}"));

    public int AttemptsFor(double expectedLat) => _attempts.GetValueOrDefault(Key(expectedLat));

    public Task<AutoReviewDecision> ReviewAsync(AutoReviewRequest request, CancellationToken ct)
    {
        Calls.Enqueue(request);
        if (request.Expected is not { } expected || !_scripts.TryGetValue(Key(expected.Lat), out var script))
            return Task.FromResult(new AutoReviewDecision(AutoReviewVerdict.Review, ["not_scripted"]));
        var attempt = _attempts.AddOrUpdate(Key(expected.Lat), 1, (_, n) => n + 1);
        return Task.FromResult(script(attempt));
    }
}

public sealed class SwitchablePublishingGate : IPublishingGate
{
    public volatile bool _enabled;
    public bool Enabled { get => _enabled; set => _enabled = value; }
}
