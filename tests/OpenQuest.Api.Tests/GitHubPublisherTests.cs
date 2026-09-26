using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Publishing;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Publishing;

namespace OpenQuest.Api.Tests;

public class GitHubPublisherTests
{
    private sealed class FakeGitHub(bool fileExists) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            if (request.Method == HttpMethod.Get)
                return fileExists
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"sha":"abc123"}""") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static PublishBatch Batch() => new("de-muenster-trees", "Datenquelle: Stadt Münster",
        [Change("genus", "\"Tilia\"")], [Change("genus", "\"Tilia\"")], new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));

    private static ApprovedContribution Change(string attr, string value) => new(
        Guid.NewGuid(), Guid.NewGuid(), "de-muenster-trees", "tree", "abc", new GeoPoint(51.9, 7.6), attr, null, value, DateTimeOffset.UnixEpoch);

    private static GitHubContributionPublisher Create(FakeGitHub handler) => new(
        new HttpClient(handler),
        Options.Create(new PublishingOptions { GitHub = new GitHubPublishingOptions { Token = "tok", Owner = "org", Repo = "data", Branch = "main", Path = "data/{source}/changes.geojson" } }));

    [Fact]
    public async Task Creates_the_file_when_it_does_not_exist_yet()
    {
        var handler = new FakeGitHub(fileExists: false);
        var result = await Create(handler).PublishAsync(Batch(), CancellationToken.None);

        var put = handler.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.EndsWith("/repos/org/data/contents/data/de-muenster-trees/changes.geojson", put.Url);
        Assert.Equal("Bearer tok", put.Auth);
        var body = JsonNode.Parse(put.Body!)!;
        Assert.Null(body["sha"]);
        Assert.Equal("main", body["branch"]!.GetValue<string>());
        Assert.Contains("Stadt Münster", Encoding.UTF8.GetString(Convert.FromBase64String(body["content"]!.GetValue<string>())));
        Assert.Equal("https://github.com/org/data/blob/main/data/de-muenster-trees/changes.geojson", result.Location);
    }

    [Fact]
    public async Task Updates_an_existing_file_using_its_sha()
    {
        var handler = new FakeGitHub(fileExists: true);
        await Create(handler).PublishAsync(Batch(), CancellationToken.None);
        var body = JsonNode.Parse(handler.Requests.Single(r => r.Method == HttpMethod.Put).Body!)!;
        Assert.Equal("abc123", body["sha"]!.GetValue<string>());
    }

    [Fact]
    public async Task Failure_response_throws_so_the_outbox_retries()
    {
        var handler = new ThrowingHandler();
        var publisher = new GitHubContributionPublisher(new HttpClient(handler),
            Options.Create(new PublishingOptions { GitHub = new GitHubPublishingOptions { Token = "tok", Owner = "o", Repo = "r" } }));
        await Assert.ThrowsAsync<HttpRequestException>(() => publisher.PublishAsync(Batch(), CancellationToken.None));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}
