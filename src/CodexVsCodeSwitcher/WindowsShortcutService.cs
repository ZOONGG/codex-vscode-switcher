using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CodexVsCodeSwitcher;

internal sealed class WindowsShortcutService
{
    public string CreateCodexVsCodeShortcut(string executablePath)
    {
        string executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The Switcher executable was not found.", executable);
        }

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            throw new DirectoryNotFoundException("The desktop directory is unavailable.");
        }

        string shortcutPath = Path.Combine(desktop, "Codex VS Code.lnk");
        Type shellLinkType = Type.GetTypeFromCLSID(
            new Guid("00021401-0000-0000-C000-000000000046"),
            throwOnError: true)!;
        var shellLink = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("The Windows shortcut service is unavailable."));
        try
        {
            shellLink.SetPath(executable);
            shellLink.SetArguments("--launch");
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            shellLink.SetDescription("Launch Codex VS Code through Codex VS Code Switcher");
            shellLink.SetIconLocation(executable, 0);
            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(shellLink);
        }

        return shortcutPath;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,
            int maximumPath,
            IntPtr findData,
            uint flags);

        void GetIdList(out IntPtr itemIdList);

        void SetIdList(IntPtr itemIdList);

        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder description,
            int maximumName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory,
            int maximumPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments,
            int maximumPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCommand(out int showCommand);

        void SetShowCommand(int showCommand);

        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath,
            int iconPathLength,
            out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
