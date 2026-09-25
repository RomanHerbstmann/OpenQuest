using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Publishing;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Publishing;

/// <summary>
/// Publishes the accepted changes as GeoJSON and CSV files under a stable location; the API serves them at
/// <c>/open-data/{dataSource}/changes.geojson</c> for the city to consume. Always on.
/// </summary>
public sealed class StorageContributionPublisher(IBlobWriter blobs) : IContributionPublisher
{
    public string Name => "storage";

    public async Task<PublishResult> PublishAsync(PublishBatch batch, CancellationToken ct)
    {
        var geo = ContributionExporter.ToGeoJson(batch.AllChanges, batch.Attribution, batch.At);
        var csv = ContributionExporter.ToCsv(batch.AllChanges, batch.Attribution, batch.At);

        await blobs.PutAsync(PublishedKeys.Run(batch.DataSourceKey, batch.At, "geojson"), geo.Content, geo.ContentType, ct);
        await blobs.PutAsync(PublishedKeys.Latest(batch.DataSourceKey, "geojson"), geo.Content, geo.ContentType, ct);
        await blobs.PutAsync(PublishedKeys.Latest(batch.DataSourceKey, "csv"), csv.Content, csv.ContentType, ct);
        return new PublishResult($"/open-data/{batch.DataSourceKey}/changes.geojson");
    }
}

/// <summary>
/// Commits the changes file to a public GitHub repository (ADR-0001: the city links such repositories from its
/// portal). Only registered when <c>Publishing:GitHub:Token</c> is configured.
/// </summary>
public sealed class GitHubContributionPublisher(HttpClient http, IOptions<PublishingOptions> options) : IContributionPublisher
{
    public string Name => "github";

    public async Task<PublishResult> PublishAsync(PublishBatch batch, CancellationToken ct)
    {
        var o = options.Value.GitHub;
        var path = o.Path.Replace("{source}", batch.DataSourceKey);
        var file = ContributionExporter.ToGeoJson(batch.AllChanges, batch.Attribution, batch.At);
        var url = $"{o.ApiUrl.TrimEnd('/')}/repos/{o.Owner}/{o.Repo}/contents/{path}";

        string? sha = null;
        using (var get = new HttpRequestMessage(HttpMethod.Get, $"{url}?ref={Uri.EscapeDataString(o.Branch)}"))
        {
            Authorize(get, o);
            using var res = await http.SendAsync(get, ct);
            if (res.StatusCode != HttpStatusCode.NotFound)
            {
                res.EnsureSuccessStatusCode();
                sha = (await res.Content.ReadFromJsonAsync<JsonObject>(ct))?["sha"]?.GetValue<string>();
            }
        }

        var body = new JsonObject
        {
            ["message"] = $"data: {batch.NewChanges.Count} accepted change(s) for {batch.DataSourceKey}",
            ["content"] = Convert.ToBase64String(file.Content),
            ["branch"] = o.Branch,
        };
        if (sha is not null) body["sha"] = sha;

        using var put = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        Authorize(put, o);
        using var putRes = await http.SendAsync(put, ct);
        putRes.EnsureSuccessStatusCode();
        return new PublishResult($"https://github.com/{o.Owner}/{o.Repo}/blob/{o.Branch}/{path}");
    }

    private static void Authorize(HttpRequestMessage request, GitHubPublishingOptions o)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }
}
