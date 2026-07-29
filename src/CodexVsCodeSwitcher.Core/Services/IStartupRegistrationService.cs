namespace CodexVsCodeSwitcher.Core.Services;

public interface IStartupRegistrationService
{
    bool IsEnabled();

    void SetEnabled(bool enabled, string executablePath);
}
