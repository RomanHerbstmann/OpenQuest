using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenQuest.Api.Tests;

/// <summary>Helpers for the public open-data feed that event handlers fill asynchronously.</summary>
internal static class Feed
{
    public const string Url = "/open-data/de-muenster-trees/changes.geojson";

    /// <summary>Waits (max 15 s) until the feed contains changes of all given submissions.</summary>
    public static async Task<JsonElement> WaitForChangesAsync(ApiFactory api, params Guid[] submissionIds)
    {
        var http = api.CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonElement last = default;
        while (DateTime.UtcNow < deadline)
        {
            var res = await http.GetAsync(Url);
            if (res.StatusCode == HttpStatusCode.OK)
            {
                last = await res.Content.ReadFromJsonAsync<JsonElement>();
                var present = last.GetProperty("features").EnumerateArray()
                    .Select(f => f.GetProperty("properties").GetProperty("submission_id").GetGuid()).ToHashSet();
                if (submissionIds.All(present.Contains)) return last;
            }
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"Feed did not contain submissions {string.Join(", ", submissionIds)} within 15 s.");
    }
}
