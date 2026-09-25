namespace OpenQuest.Adapters.Muenster;

public class MuensterOptions
{
    public const string Section = "Adapters:DeMuenster";

    /// <summary>WFS endpoint of the Amt für Grünflächen (layer <c>Baeume</c>).</summary>
    public string WfsUrl { get; set; } = "https://geo.stadt-muenster.de/mapserv/odgruen_serv";
    public int TimeoutSeconds { get; set; } = 180;
}
