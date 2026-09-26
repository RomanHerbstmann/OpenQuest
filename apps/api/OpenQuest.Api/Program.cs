using System.Text.Json;
using System.Text.Json.Serialization;
using OpenQuest.Api.Composition;
using OpenQuest.Api.Startup;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services
    .AddOpenQuestOptions(config)
    .AddOpenQuestPersistence(config)
    .AddOpenQuestAuth(config, builder.Environment)
    .AddOpenQuestStorage()
    .AddOpenQuestGame()
    .AddOpenQuestGamification()
    .AddOpenQuestEventing()
    .AddOpenQuestAutoReview(config)
    .AddOpenQuestPublishing(config)
    .AddOpenQuestStartupTasks();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 12 * 1024 * 1024);

var origins = (config["Cors:Origins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();

// The built-in admin panel (static files in wwwroot/panel) is served under /panel/. Its scripts, styles and Leaflet are local;
// only the map tiles come from OpenStreetMap.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/panel")
    {
        context.Response.Redirect("/panel/"); // a middleware, not an endpoint: an endpoint for /panel would also match /panel/
        return;
    }
    if (context.Request.Path.StartsWithSegments("/panel"))
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data: blob: https://tile.openstreetmap.org; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.CacheControl = "no-cache";
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapOpenQuestApi();

await app.Services.GetRequiredService<StartupTaskRunner>().RunAsync();
app.Run();

public partial class Program;
