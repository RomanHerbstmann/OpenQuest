using OpenQuest.Api.Data;
using OpenQuest.Core.Adapters;

namespace OpenQuest.Api.Sync;

/// <summary>Resolves the adapter selected by configuration (Adapters:Active).</summary>
public interface IAdapterProvider
{
    IDataSourceAdapter Active { get; }
}

/// <summary>Starts imports without waiting for them.</summary>
public interface ISyncTrigger
{
    /// <summary>
    /// False if a sync is already running. <paramref name="acceptSchemaChange"/> lets the run go on although the source's
    /// field list differs from the last successful run (only after somebody checked that the adapter still copes).
    /// </summary>
    bool TryStartInBackground(bool acceptSchemaChange = false);
}

/// <summary>Runs an import and returns its outcome; null if one is already running.</summary>
public interface IAssetSynchronizer
{
    Task<IReadOnlyList<SyncRun>?> RunAsync(bool acceptSchemaChange, CancellationToken ct);
}

public interface ISyncStatus
{
    bool IsRunning { get; }
}
