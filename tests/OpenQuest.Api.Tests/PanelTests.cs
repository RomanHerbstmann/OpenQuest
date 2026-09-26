using System.Net;
using System.Text.RegularExpressions;

namespace OpenQuest.Api.Tests;

/// <summary>The built-in admin panel is a set of static files under /panel. These tests catch broken paths and a missing security policy.</summary>
[Collection(ApiCollection.Name)]
public partial class PanelTests(ApiFactory api)
{
    [GeneratedRegex("""(?:src|href)="([^"]+)""")] private static partial Regex Reference();

    [Fact]
    public async Task The_panel_is_served_without_login_with_a_content_security_policy()
    {
        var http = api.CreateClient(new() { AllowAutoRedirect = false });

        var redirect = await http.GetAsync("/panel");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/panel/", redirect.Headers.Location?.OriginalString);

        var page = await http.GetAsync("/panel/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("OpenQuest Admin", await page.Content.ReadAsStringAsync());
        var csp = string.Join(";", page.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("connect-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Everything_the_page_references_exists()
    {
        var http = api.CreateClient();
        var html = await http.GetStringAsync("/panel/");
        var files = Reference().Matches(html).Select(m => m.Groups[1].Value).Where(u => !u.StartsWith("http")).ToList();
        Assert.Contains("app.js", files);
        Assert.Contains("vendor/leaflet/leaflet.js", files);

        // the modules the app imports are served too
        files.AddRange(["api.js", "dom.js", "de.js", "districts.js", "moderation.js", "vendor/leaflet/images/marker-icon.png"]);
        foreach (var file in files.Distinct())
        {
            var res = await http.GetAsync("/panel/" + file);
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"/panel/{file} -> {(int)res.StatusCode}");
        }
        Assert.Equal("text/javascript", (await http.GetAsync("/panel/app.js")).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_api_is_not_affected_by_the_static_files()
    {
        var http = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/admin/cities")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/panel/does-not-exist.js")).StatusCode);
    }
}
