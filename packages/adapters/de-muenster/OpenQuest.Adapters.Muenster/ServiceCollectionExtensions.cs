using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenQuest.Core.Adapters;

namespace OpenQuest.Adapters.Muenster;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMuensterAdapter(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MuensterOptions>(config.GetSection(MuensterOptions.Section));
        services.AddHttpClient<MuensterAdapter>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(config.GetValue($"{MuensterOptions.Section}:TimeoutSeconds", 180));
            c.DefaultRequestHeaders.UserAgent.ParseAdd("OpenQuest/0.1 (+https://github.com/RomanHerbstmann/OpenQuest)");
        });
        services.AddTransient<IDataSourceAdapter>(sp => sp.GetRequiredService<MuensterAdapter>());
        return services;
    }
}
