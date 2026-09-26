using System.Net;
using System.Net.Http.Json;

namespace OpenQuest.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Register_login_and_me()
    {
        var (client, username, codes) = await api.RegisterAsync("alice");
        Assert.Equal(8, codes.Length);

        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/me");
        Assert.Equal(username, me.GetProperty("username").GetString());
        Assert.Equal("player", me.GetProperty("role").GetString());

        var login = await api.CreateClient().PostAsJsonAsync("/auth/login", new { username = username.ToUpperInvariant(), password = "correcthorse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_are_rejected_alike()
    {
        var (_, username, _) = await api.RegisterAsync("bob");
        var http = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/auth/login", new { username, password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/auth/login", new { username = "nobody-here", password = "whatever123" })).StatusCode);
    }

    [Fact]
    public async Task Duplicate_username_and_weak_input_are_rejected()
    {
        var (_, username, _) = await api.RegisterAsync("dup");
        var http = api.CreateClient();
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/auth/register", new { username, password = "correcthorse" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/auth/register", new { username = "ab", password = "correcthorse" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/auth/register", new { username = "validname", password = "short" })).StatusCode);
    }

    [Fact]
    public async Task Recovery_code_resets_password_once_and_regeneration_invalidates_old_codes()
    {
        var (client, username, codes) = await api.RegisterAsync("carol");
        var http = api.CreateClient();

        var loose = codes[0].ToLowerInvariant().Replace('-', ' '); // players type loosely
        var ok = await http.PostAsJsonAsync("/auth/recover", new { username, recoveryCode = loose, newPassword = "brand-new-pw" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/auth/recover", new { username, recoveryCode = codes[0], newPassword = "another-pw-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/auth/login", new { username, password = "brand-new-pw" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/auth/login", new { username, password = "correcthorse" })).StatusCode);

        // regenerate: needs the current password, replaces all codes
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/me/recovery-codes", new { password = "wrong" })).StatusCode);
        var fresh = await client.PostAsJsonAsync("/me/recovery-codes", new { password = "brand-new-pw" });
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/auth/recover", new { username, recoveryCode = codes[1], newPassword = "yet-another-1" })).StatusCode);
    }

    [Fact]
    public async Task Endpoints_need_the_right_role()
    {
        var (player, _, _) = await api.RegisterAsync("plain");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/quests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/submissions")).StatusCode);
        var admin = await api.AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/quests")).StatusCode);
    }
}
