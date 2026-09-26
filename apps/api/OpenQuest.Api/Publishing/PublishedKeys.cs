namespace OpenQuest.Api.Publishing;

/// <summary>Where published files live in the blob store.</summary>
public static class PublishedKeys
{
    /// <summary>Stable location of the current file (the URL handed to the city).</summary>
    public static string Latest(string dataSourceKey, string extension) => $"published/{dataSourceKey}/changes.{extension}";

    public static string Run(string dataSourceKey, DateTimeOffset at, string extension)
        => $"published/{dataSourceKey}/runs/{at:yyyyMMdd-HHmmss}.{extension}";
}
