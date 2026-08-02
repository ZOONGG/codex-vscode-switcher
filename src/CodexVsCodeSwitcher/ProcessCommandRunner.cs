using System.Diagnostics;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class ProcessCommandRunner : IProcessCommandRunner
{
    public async Task<int> RunAsync(VsCodeProcessStartSpec startSpec, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(ManagedProcessStartInfoFactory.Create(startSpec))
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
