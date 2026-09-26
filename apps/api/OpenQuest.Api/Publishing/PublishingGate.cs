using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;

namespace OpenQuest.Api.Publishing;

/// <summary>Whether accepted changes are pushed to open data at all (<c>Publishing:Enabled</c>, off by default, ADR-0014).</summary>
public interface IPublishingGate
{
    bool Enabled { get; }
}

public sealed class ConfigPublishingGate(IOptions<PublishingOptions> options) : IPublishingGate
{
    public bool Enabled => options.Value.Enabled;
}
