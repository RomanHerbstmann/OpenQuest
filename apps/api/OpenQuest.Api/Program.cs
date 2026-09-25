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
    .AddOpenQuestEventing()
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
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapOpenQuestApi();

await app.Services.GetRequiredService<StartupTaskRunner>().RunAsync();
app.Run();

public partial class Program;
