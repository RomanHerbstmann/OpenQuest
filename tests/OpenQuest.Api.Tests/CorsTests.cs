using System.Net;

namespace OpenQuest.Api.Tests;

/// <summary>The phone app's web view has its own origins; a browser-style request from there must be allowed by CORS.</summary>
[Collection(ApiCollection.Name)]
public class CorsTests(ApiFactory api)
{
    [Theory]
    [InlineData("https://localhost")]        // Android web view
    [InlineData("capacitor://localhost")]    // iOS web view
    [InlineData("http://localhost:3000")]    // web frontend in development
    public async Task Allowed_origins_may_call_the_api_including_the_authorization_header(string origin)
    {
        var http = api.CreateClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/me/claims");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var res = await http.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(origin, res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("authorization", string.Join(",", res.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Other_origins_get_no_cors_headers()
    {
        var http = api.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.example");
        var res = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
