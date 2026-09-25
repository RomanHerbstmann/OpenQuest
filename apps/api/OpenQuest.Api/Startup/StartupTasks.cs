using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Api.Sync;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Startup;

/// <summary>Work that runs once at start-up, in registration order. Add a task by registering another one.</summary>
public interface IStartupTask
{
    Task RunAsync(CancellationToken ct);
}

public sealed class MigrateDatabaseTask(AppDbContext db, IConfiguration config) : IStartupTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (config.GetValue("Database:MigrateOnStartup", true)) await db.Database.MigrateAsync(ct);
    }
}

/// <summary>
/// Keeps the reference tables (task types, asset types, their relations and the data sources of the active adapter)
/// in sync with the definitions in code. Idempotent.
/// </summary>
public sealed class SeedCatalogTask(AppDbContext db, IAdapterProvider adapters) : IStartupTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        var taskTypes = await db.TaskTypes.ToDictionaryAsync(t => t.Key, ct);
        foreach (var def in TaskTypes.All)
        {
            if (!taskTypes.TryGetValue(def.Key, out var row))
                db.TaskTypes.Add(row = taskTypes[def.Key] = new TaskTypeEntity { Key = def.Key });
            row.Name = def.Name;
            row.ConfigSchema = def.ConfigSchemaJson;
            row.ResultSchema = def.ResultSchemaJson;
        }

        var assetTypes = await db.AssetTypes.ToDictionaryAsync(t => t.Key, ct);
        foreach (var def in AssetType.Known)
        {
            if (!assetTypes.TryGetValue(def.Key, out var row))
                db.AssetTypes.Add(row = assetTypes[def.Key] = new AssetTypeEntity { Key = def.Key });
            row.Name = def.Name;
            row.Icon = def.Icon;
            row.AttributeSchema = def.AttributeSchemaJson;
        }
        await db.SaveChangesAsync(ct);

        var links = await db.AssetTypeTaskTypes.ToListAsync(ct);
        foreach (var def in AssetType.Known)
            foreach (var task in def.AllowedTaskTypes)
            {
                var (a, t) = (assetTypes[def.Key].Id, taskTypes[task.Key()].Id);
                if (!links.Any(l => l.AssetTypeId == a && l.TaskTypeId == t))
                    db.AssetTypeTaskTypes.Add(new AssetTypeTaskType { AssetTypeId = a, TaskTypeId = t });
            }

        var adapter = adapters.Active;
        var sources = await db.DataSources.ToDictionaryAsync(s => s.Key, ct);
        foreach (var d in adapter.DataSources)
        {
            if (!sources.TryGetValue(d.Key, out var row))
                db.DataSources.Add(row = new DataSource { Key = d.Key });
            row.AdapterKey = adapter.Id;
            row.Name = d.Name;
            row.City = d.City;
            row.SourceUrl = d.SourceUrl;
            row.License = d.License;
            row.Attribution = d.Attribution;
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Creates the first admin account from configuration if it does not exist yet.</summary>
public sealed class SeedAdminTask(
    AppDbContext db, IPasswordService passwords, IOptions<AdminOptions> options, IHostEnvironment env, ILogger<SeedAdminTask> log) : IStartupTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var username = o.Username?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(username))
        {
            log.LogInformation("Admin:Username not set, skipping admin seeding.");
            return;
        }
        if (await db.Users.AnyAsync(u => u.Username == username, ct)) return;

        var password = o.Password;
        var generated = false;
        if (string.IsNullOrEmpty(password))
        {
            if (!env.IsDevelopment()) throw new InvalidOperationException("Admin:Password must be set outside Development.");
            password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            generated = true;
        }

        db.Users.Add(new User { Username = username, Role = UserRole.Admin, PasswordHash = passwords.Hash(password) });
        await db.SaveChangesAsync(ct);
        if (generated) log.LogWarning("Created admin '{Username}' with generated password: {Password}  (set Admin__Password to choose your own)", username, password);
        else log.LogInformation("Seeded admin user '{Username}'.", username);
    }
}

public sealed class StartupTaskRunner(IServiceProvider services)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        foreach (var taskType in services.GetRequiredService<IEnumerable<StartupTaskDescriptor>>().Select(d => d.Type))
        {
            using var scope = services.CreateScope();
            await ((IStartupTask)scope.ServiceProvider.GetRequiredService(taskType)).RunAsync(ct);
        }
    }
}

public sealed record StartupTaskDescriptor(Type Type);
