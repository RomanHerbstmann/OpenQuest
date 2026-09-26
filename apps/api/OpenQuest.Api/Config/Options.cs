namespace OpenQuest.Api.Config;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "openquest";
    public string Audience { get; set; } = "openquest";
    public int ExpiryMinutes { get; set; } = 60 * 24 * 7;
}

public class AuthOptions
{
    public const string Section = "Auth";
    // OWASP baseline for argon2id: m=19 MiB, t=2, p=1
    public int Argon2MemoryKiB { get; set; } = 19456;
    public int Argon2TimeCost { get; set; } = 2;
    public int Argon2Parallelism { get; set; } = 1;
    /// <summary>Requests per minute and IP on /auth/* (login, register, recovery: codes are the only recovery path).</summary>
    public int RateLimitPerMinute { get; set; } = 10;
}

public class AdminOptions
{
    public const string Section = "Admin";
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class GameOptions
{
    public const string Section = "Game";
    /// <summary>Defaults for new quests (stored per quest as geofence_radius_m / claim_ttl_minutes).</summary>
    public int GeofenceMeters { get; set; } = 30;
    public int ClaimTimeoutMinutes { get; set; } = 30;
    public int MaxNearbyResults { get; set; } = 200;
    public double MaxNearbyRadiusMeters { get; set; } = 5000;
}

public class GamificationOptions
{
    public const string Section = "Gamification";
    /// <summary>Points at which each level starts, ascending, first entry 0. Empty = the core's default curve.</summary>
    public int[] LevelThresholds { get; set; } = [];
    /// <summary>
    /// Two districts of a city may share a border but not an area. Overlaps up to this share of the smaller district are
    /// tolerated, because neighbouring outlines from different sources rarely match to the last decimal.
    /// </summary>
    public double OverlapToleranceRatio { get; set; } = 0.001;
}

public class StorageOptions
{
    public const string Section = "Storage";
    public string ServiceUrl { get; set; } = "http://localhost:9000";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "openquest-photos";
    public long MaxPhotoBytes { get; set; } = 10 * 1024 * 1024;
}

public class OutboxOptions
{
    public const string Section = "Outbox";
    public int BatchSize { get; set; } = 50;
    /// <summary>After this many failed deliveries a message is parked as "dead" (see /admin/outbox).</summary>
    public int MaxAttempts { get; set; } = 8;
    /// <summary>First retry delay; doubles with every attempt (5 s, 10 s, 20 s, ...).</summary>
    public int RetryBaseDelayMs { get; set; } = 5000;
}

public class PublishingOptions
{
    public const string Section = "Publishing";
    public GitHubPublishingOptions GitHub { get; set; } = new();
}

/// <summary>Optional publication of the changes file to a public GitHub repository (see ADR-0001, "Rückkanal").</summary>
public class GitHubPublishingOptions
{
    /// <summary>Personal access token with contents:write on the repository. Empty = GitHub publishing is off.</summary>
    public string? Token { get; set; }
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Branch { get; set; } = "main";
    /// <summary>Path of the file in the repository; {source} is replaced by the data source key.</summary>
    public string Path { get; set; } = "data/{source}/changes.geojson";
    public string ApiUrl { get; set; } = "https://api.github.com";
}
