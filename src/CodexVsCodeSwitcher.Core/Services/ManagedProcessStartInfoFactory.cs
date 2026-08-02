using System.Diagnostics;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public static class ManagedProcessStartInfoFactory
{
    public static ProcessStartInfo Create(VsCodeProcessStartSpec startSpec)
    {
        ArgumentNullException.ThrowIfNull(startSpec);
        var startInfo = new ProcessStartInfo
        {
            FileName = startSpec.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(startSpec.ExecutablePath) ?? string.Empty,
        };
        foreach (string argument in startSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Accessing Environment preserves the inherited parent environment. Only the
        // explicit application-owned overrides are changed for the managed child.
        foreach ((string name, string value) in startSpec.EnvironmentOverrides)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }
}
