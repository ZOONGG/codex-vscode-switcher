using System.Diagnostics;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class ProcessCommandRunner : IProcessCommandRunner
{
    public async Task<int> RunAsync(VsCodeProcessStartSpec startSpec, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(CreateStartInfo(startSpec))
            ?? throw new InvalidOperationException("The VS Code command could not be started.");
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(VsCodeProcessStartSpec startSpec)
    {
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

        foreach ((string name, string value) in startSpec.EnvironmentOverrides)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
