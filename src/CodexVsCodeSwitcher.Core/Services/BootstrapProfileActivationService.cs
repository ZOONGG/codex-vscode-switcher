namespace CodexVsCodeSwitcher.Core.Services;

public sealed class BootstrapProfileActivationService
{
    public const string MessageKey = "VsCodeSwitchingNotImplemented";

    public Task<BootstrapActivationResult> ActivateAsync(string profileName, CancellationToken cancellationToken = default)
    {
        _ = ProfileName.RequireValid(profileName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BootstrapActivationResult(false, MessageKey));
    }
}

public sealed record BootstrapActivationResult(bool Succeeded, string MessageKey);
