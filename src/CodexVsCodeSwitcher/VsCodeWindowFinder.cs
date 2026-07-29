using System.Diagnostics;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class VsCodeWindowFinder
{
    private readonly VsCodeWindowSelectionService selection = new();

    public IntPtr FindMainWindow(string? customExecutablePath, bool includeStandardExecutables)
    {
        var candidates = new List<VsCodeWindowCandidate>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (!TryResolveExecutable(
                    process,
                    customExecutablePath,
                    includeStandardExecutables,
                    out string executablePath))
                {
                    continue;
                }

                CollectProcessWindows(process.Id, executablePath, candidates);
            }
        }

        return selection.Select(candidates, customExecutablePath, includeStandardExecutables)?.Handle ?? IntPtr.Zero;
    }

    private bool TryResolveExecutable(
        Process process,
        string? customExecutablePath,
        bool includeStandardExecutables,
        out string executablePath)
    {
        executablePath = string.Empty;
        string processName;
        try
        {
            processName = process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        string fallbackFileName = processName + ".exe";
        bool standardName = selection.IsSupportedExecutable(
            fallbackFileName,
            customExecutablePath: null,
            includeStandardExecutables);
        string? customFileName = string.IsNullOrWhiteSpace(customExecutablePath)
            ? null
            : Path.GetFileName(customExecutablePath);
        if (!standardName && !fallbackFileName.Equals(customFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            executablePath = process.MainModule?.FileName ?? string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        catch (InvalidOperationException)
        {
        }

        if (executablePath.Length == 0)
        {
            if (!standardName)
            {
                return false;
            }

            executablePath = fallbackFileName;
        }

        return selection.IsSupportedExecutable(
            executablePath,
            customExecutablePath,
            includeStandardExecutables);
    }

    private static void CollectProcessWindows(
        int processId,
        string executablePath,
        ICollection<VsCodeWindowCandidate> candidates)
    {
        _ = NativeMethods.EnumWindows((handle, callbackState) =>
        {
            _ = callbackState;
            _ = NativeMethods.GetWindowThreadProcessId(handle, out uint windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            bool visible = NativeMethods.IsWindowVisible(handle);
            bool topLevel = NativeMethods.GetWindow(handle, NativeMethods.GwOwner) == IntPtr.Zero;
            _ = NativeMethods.GetWindowRect(handle, out NativeRect bounds);
            candidates.Add(new VsCodeWindowCandidate(
                processId,
                executablePath,
                handle,
                visible,
                topLevel,
                bounds.Width,
                bounds.Height));
            return true;
        }, IntPtr.Zero);
    }
}
