using CodexVsCodeSwitcher.Core;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed record UpdateChannelConfiguration(string Identity, Uri? Endpoint)
{
    public bool IsConfigured => Endpoint is not null;

    public static UpdateChannelConfiguration Unconfigured { get; } =
        new(ProductIdentity.UpdateChannelIdentity, null);
}
