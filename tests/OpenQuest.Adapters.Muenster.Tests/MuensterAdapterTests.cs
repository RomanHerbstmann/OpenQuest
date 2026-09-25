using System.Net;
using Microsoft.Extensions.Options;
using OpenQuest.Adapters.Muenster;
using OpenQuest.Core.Adapters;
using OpenQuest.Core.Domain;

namespace OpenQuest.Adapters.Muenster.Tests;

public class MuensterAdapterTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "baeume-sample.json");

    private sealed class FixedResponse(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private static MuensterAdapter Create(FixedResponse handler)
        => new(new HttpClient(handler), Options.Create(new MuensterOptions { WfsUrl = "https://wfs.example/odgruen" }));

    [Fact]
    public async Task Snapshot_keeps_the_untouched_download_and_describes_it()
    {
        var raw = await File.ReadAllBytesAsync(FixturePath);
        var handler = new FixedResponse(raw);
        var snapshot = await Create(handler).FetchSnapshotAsync(new AssetQuery(AssetType.Tree));

        Assert.Equal(raw, snapshot.RawContent);                       // exactly as delivered
        Assert.Equal("geojson", snapshot.RawFileExtension);
        Assert.Equal(40, snapshot.Assets.Count);
        Assert.Equal(40, snapshot.RecordCount);
        Assert.Equal(["baumgruppe", "str_schl"], snapshot.SourceFields);
        Assert.Equal(SourceSnapshot.Hash(["str_schl", "baumgruppe"]), snapshot.SchemaHash); // order independent
        Assert.Contains("SRSNAME=EPSG:25832", handler.Urls.Single());
    }

    [Fact]
    public async Task A_broken_response_fails_instead_of_returning_an_empty_snapshot()
    {
        var adapter = Create(new FixedResponse("<html>maintenance</html>"u8.ToArray()));
        await Assert.ThrowsAnyAsync<Exception>(() => adapter.FetchSnapshotAsync(new AssetQuery(AssetType.Tree)));
    }

    [Fact]
    public async Task Http_errors_fail_loudly()
    {
        var adapter = Create(new FixedResponse([], HttpStatusCode.BadGateway));
        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.FetchSnapshotAsync(new AssetQuery(AssetType.Tree)));
    }

    [Fact]
    public async Task Bounding_box_filters_assets_but_not_the_raw_download()
    {
        var raw = await File.ReadAllBytesAsync(FixturePath);
        var snapshot = await Create(new FixedResponse(raw))
            .FetchSnapshotAsync(new AssetQuery(AssetType.Tree, new BBox(0, 0, 1, 1)));
        Assert.Empty(snapshot.Assets);
        Assert.Equal(raw, snapshot.RawContent);
    }

    [Fact]
    public void Adapter_describes_its_data_source_with_license_and_attribution()
    {
        var source = Create(new FixedResponse([])).DataSources.Single();
        Assert.Equal("de-muenster-trees", source.Key);
        Assert.Equal("dl-de/by-2.0", source.License);
        Assert.Contains("Stadt Münster", source.Attribution);
    }
}
