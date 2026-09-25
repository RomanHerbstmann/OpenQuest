using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<AssetTypeEntity> AssetTypes => Set<AssetTypeEntity>();
    public DbSet<TaskTypeEntity> TaskTypes => Set<TaskTypeEntity>();
    public DbSet<AssetTypeTaskType> AssetTypeTaskTypes => Set<AssetTypeTaskType>();
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();
    public DbSet<AssetSnapshot> AssetSnapshots => Set<AssetSnapshot>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRecoveryCode> UserRecoveryCodes => Set<UserRecoveryCode>();
    public DbSet<QuestCampaign> QuestCampaigns => Set<QuestCampaign>();
    public DbSet<Quest> Quests => Set<Quest>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<AttributeChange> AttributeChanges => Set<AttributeChange>();
    public DbSet<ExportRun> ExportRuns => Set<ExportRun>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<ClaimStatus>().HaveConversion<SnakeEnumConverter<ClaimStatus>>().HaveMaxLength(24);
        c.Properties<SubmissionStatus>().HaveConversion<SnakeEnumConverter<SubmissionStatus>>().HaveMaxLength(24);
        c.Properties<UserRole>().HaveConversion<SnakeEnumConverter<UserRole>>().HaveMaxLength(24);
        c.Properties<QuestStatus>().HaveConversion<SnakeEnumConverter<QuestStatus>>().HaveMaxLength(24);
        c.Properties<AssetChangeType>().HaveConversion<SnakeEnumConverter<AssetChangeType>>().HaveMaxLength(24);
        c.Properties<AssetStatus>().HaveConversion<SnakeEnumConverter<AssetStatus>>().HaveMaxLength(24);
        c.Properties<OutboxStatus>().HaveConversion<SnakeEnumConverter<OutboxStatus>>().HaveMaxLength(24);
        c.Properties<ChangeStatus>().HaveConversion<SnakeEnumConverter<ChangeStatus>>().HaveMaxLength(24);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("postgis");

        b.Entity<DataSource>(e =>
        {
            e.ToTable("data_source");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Config).HasColumnType("jsonb");
        });

        b.Entity<SyncRun>(e =>
        {
            e.ToTable("sync_run");
            e.HasOne(x => x.DataSource).WithMany().HasForeignKey(x => x.DataSourceId);
            e.HasIndex(x => new { x.DataSourceId, x.StartedAt });
            e.Property(x => x.SnapshotKey).HasMaxLength(256);
            e.Property(x => x.SchemaHash).HasMaxLength(64);
        });

        b.Entity<AssetSnapshot>(e =>
        {
            e.ToTable("asset_snapshot");
            e.HasOne<AssetEntity>().WithMany().HasForeignKey(x => x.AssetId);
            e.HasOne<SyncRun>().WithMany().HasForeignKey(x => x.SyncRunId);
            e.HasIndex(x => new { x.AssetId, x.SyncRunId }).IsUnique();
            e.HasIndex(x => x.SyncRunId);
            e.Property(x => x.Geom).HasColumnType("geography (point)");
            e.Property(x => x.Raw).HasColumnType("jsonb");
            e.Property(x => x.SourceHash).HasMaxLength(64);
        });

        b.Entity<AssetTypeEntity>(e =>
        {
            e.ToTable("asset_type");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.AttributeSchema).HasColumnType("jsonb");
        });

        b.Entity<TaskTypeEntity>(e =>
        {
            e.ToTable("task_type");
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.ConfigSchema).HasColumnType("jsonb");
            e.Property(x => x.ResultSchema).HasColumnType("jsonb");
        });

        b.Entity<AssetTypeTaskType>(e =>
        {
            e.ToTable("asset_type_task_type");
            e.HasKey(x => new { x.AssetTypeId, x.TaskTypeId });
            e.HasOne<AssetTypeEntity>().WithMany().HasForeignKey(x => x.AssetTypeId);
            e.HasOne<TaskTypeEntity>().WithMany().HasForeignKey(x => x.TaskTypeId);
        });

        b.Entity<AssetEntity>(e =>
        {
            e.ToTable("asset");
            e.HasOne(x => x.AssetType).WithMany().HasForeignKey(x => x.AssetTypeId);
            e.HasOne<DataSource>().WithMany().HasForeignKey(x => x.DataSourceId);
            e.HasIndex(x => new { x.DataSourceId, x.ExternalId }).IsUnique();
            e.Property(x => x.Geom).HasColumnType("geography (point)");
            e.HasIndex(x => x.Geom).HasMethod("gist");
            e.Property(x => x.Attributes).HasColumnType("jsonb");
            e.Property(x => x.Raw).HasColumnType("jsonb");
            e.Property(x => x.SourceHash).HasMaxLength(64);
            e.HasIndex(x => x.Attributes).HasMethod("gin").HasOperators("jsonb_path_ops");
            e.HasIndex(x => new { x.AssetTypeId, x.Status });
        });

        b.Entity<User>(e =>
        {
            e.ToTable("user");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(64);
            e.Property(x => x.Locale).HasMaxLength(8);
        });

        b.Entity<UserRecoveryCode>(e =>
        {
            e.ToTable("user_recovery_code");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<QuestCampaign>(e =>
        {
            e.ToTable("quest_campaign");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy);
            e.Property(x => x.AssetFilter).HasColumnType("jsonb");
        });

        b.Entity<Quest>(e =>
        {
            e.ToTable("quest");
            e.HasOne<QuestCampaign>().WithMany().HasForeignKey(x => x.CampaignId);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId);
            e.HasOne(x => x.TaskType).WithMany().HasForeignKey(x => x.TaskTypeId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy);
            e.Property(x => x.TaskConfig).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Status, x.AssetId });
            e.HasIndex(x => x.AssetId);
            e.ToTable(t => t.HasCheckConstraint("ck_quest_slots", "slots_taken >= 0 AND max_completions >= 1"));
        });

        b.Entity<Claim>(e =>
        {
            e.ToTable("claim");
            e.HasOne(x => x.Quest).WithMany().HasForeignKey(x => x.QuestId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => new { x.QuestId, x.Status });
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => x.ExpiresAt).HasFilter("status = 'active'");
            // At most one live claim per user and quest. (The ERD says 'active'; 'submitted' is included so a
            // player cannot claim again and hand in a second submission for the same quest.)
            e.HasIndex(x => new { x.QuestId, x.UserId }).IsUnique().HasFilter("status IN ('active','submitted')");
        });

        b.Entity<Submission>(e =>
        {
            e.ToTable("submission");
            e.HasOne(x => x.Claim).WithMany().HasForeignKey(x => x.ClaimId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedBy);
            e.HasIndex(x => x.ClaimId).IsUnique();
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Location).HasColumnType("geography (point)");
            e.HasIndex(x => new { x.Status, x.SubmittedAt });
        });

        b.Entity<Media>(e =>
        {
            e.ToTable("media");
            e.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId);
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Phash).HasMaxLength(16);
            e.Property(x => x.StorageKey).HasMaxLength(256);
        });

        b.Entity<AttributeChange>(e =>
        {
            e.ToTable("attribute_change");
            e.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId);
            e.HasOne<ExportRun>().WithMany().HasForeignKey(x => x.ExportRunId);
            e.Property(x => x.OldValue).HasColumnType("jsonb");
            e.Property(x => x.NewValue).HasColumnType("jsonb");
            e.Property(x => x.AttributeKey).HasMaxLength(64);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubmissionId);
        });

        b.Entity<ExportRun>(e =>
        {
            e.ToTable("export_run");
            e.HasOne(x => x.DataSource).WithMany().HasForeignKey(x => x.DataSourceId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.Property(x => x.Type).HasMaxLength(128);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AvailableAt, x.OccurredAt }).HasFilter("status = 'pending'");
        });
    }
}

/// <summary>Stores enums as snake_case strings ("removed_at_source"), matching the ERD.</summary>
public class SnakeEnumConverter<T>() : ValueConverter<T, string>(
    v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()),
    s => Parse(s)) where T : struct, Enum
{
    private static T Parse(string s)
    {
        foreach (var v in Enum.GetValues<T>())
            if (JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()) == s) return v;
        throw new InvalidOperationException($"'{s}' is not a valid {typeof(T).Name}.");
    }
}
