using System.Drawing;
using System.Windows.Forms;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class TrayIconService : IDisposable
{
    private readonly Icon icon;
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip menu = new();
    private readonly Localizer localizer;
    private readonly TrayClickCoordinator clickCoordinator = new();
    private readonly System.Windows.Forms.Timer singleClickTimer = new() { Interval = 60 };
    private IReadOnlyList<ProfileInfo> profiles = [];
    private string? activeProfile;
    private bool overlayVisible;
    private bool startWithWindows;
    private WorkspaceHistorySnapshot projects = new(null, []);
    private bool disposed;

    public TrayIconService(Localizer localizer)
    {
        this.localizer = localizer;
        icon = AppIcons.CreateNotifyIcon();
        notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Codex VS Code Switcher",
            Visible = true,
            ContextMenuStrip = menu,
        };
        notifyIcon.MouseClick += OnMouseClick;
        notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        singleClickTimer.Tick += (_, _) =>
        {
            if (clickCoordinator.TryConsumeDueSingleClick(DateTimeOffset.UtcNow))
            {
                ToggleOverlayRequested?.Invoke();
            }
        };
        localizer.LanguageChanged += RebuildMenu;
        RebuildMenu();
    }

    public event Action? ToggleOverlayRequested;

    public event Action? OpenCodexRequested;

    public event Action? SettingsRequested;

    public event Action? LaunchManagedVsCodeRequested;

    public event Action? RestartManagedVsCodeRequested;

    public event Action? InstallCodexExtensionRequested;

    public event Action<bool>? StartWithWindowsChanged;

    public event Action<string>? ProfileSelected;

    public event Action<string>? ProjectSelected;

    public event Action<string>? RemoveRecentProjectRequested;

    public event Action? ClearRecentProjectsRequested;

    public event Action? ExitRequested;

    public void UpdateProfiles(IReadOnlyList<ProfileInfo> newProfiles, string? newActiveProfile)
    {
        if (ReferenceEquals(profiles, newProfiles) && string.Equals(activeProfile, newActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        profiles = newProfiles;
        activeProfile = newActiveProfile;
        RebuildMenu();
    }

    public void UpdateProjects(WorkspaceHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        projects = snapshot;
        RebuildMenu();
    }

    public void UpdateOverlayState(bool isVisible)
    {
        if (overlayVisible == isVisible)
        {
            return;
        }

        overlayVisible = isVisible;
        RebuildMenu();
    }

    public void UpdateStartWithWindows(bool isEnabled)
    {
        if (startWithWindows == isEnabled)
        {
            return;
        }

        startWithWindows = isEnabled;
        RebuildMenu();
    }

    public void ShowBalloon(string title, string message)
    {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        localizer.LanguageChanged -= RebuildMenu;
        singleClickTimer.Stop();
        singleClickTimer.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        icon.Dispose();
        menu.Dispose();
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            clickCoordinator.RegisterLeftClick(DateTimeOffset.UtcNow);
            singleClickTimer.Start();
        }
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            clickCoordinator.RegisterLeftDoubleClick();
            singleClickTimer.Stop();
            OpenCodexRequested?.Invoke();
        }
    }

    private void RebuildMenu()
    {
        menu.Items.Clear();
        menu.Items.Add(localizer["LaunchManagedVsCode"], null, (_, _) => LaunchManagedVsCodeRequested?.Invoke());
        var restart = new ToolStripMenuItem(localizer["RestartManagedVsCode"])
        {
            Enabled = !string.IsNullOrWhiteSpace(activeProfile),
        };
        restart.Click += (_, _) => RestartManagedVsCodeRequested?.Invoke();
        menu.Items.Add(restart);
        menu.Items.Add(localizer["InstallCodexExtension"], null, (_, _) => InstallCodexExtensionRequested?.Invoke());
        menu.Items.Add(overlayVisible ? localizer["HideSwitcher"] : localizer["ShowSwitcher"], null, (_, _) => ToggleOverlayRequested?.Invoke());

        var profilesMenu = new ToolStripMenuItem(localizer["Profiles"]);
        foreach (ProfileInfo profile in profiles)
        {
            var item = new ToolStripMenuItem(profile.DisplayName)
            {
                Checked = string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase),
                Enabled = profile.IsEligibleForSwitching
                    && !string.Equals(profile.Name, activeProfile, StringComparison.OrdinalIgnoreCase),
                ToolTipText = profile.ValidationStatus switch
                {
                    ProfileValidationStatus.Valid => localizer["ProfileValid"],
                    ProfileValidationStatus.Invalid => localizer["ProfileInvalid"],
                    _ => localizer["ProfileIncomplete"],
                },
            };
            string profileName = profile.Name;
            item.Click += (_, _) => ProfileSelected?.Invoke(profileName);
            profilesMenu.DropDownItems.Add(item);
        }

        if (profilesMenu.DropDownItems.Count == 0)
        {
            profilesMenu.DropDownItems.Add(new ToolStripMenuItem(localizer["NoReadyProfiles"]) { Enabled = false });
        }

        menu.Items.Add(profilesMenu);
        var projectsMenu = new ToolStripMenuItem(localizer["RecentProjects"]);
        foreach (WorkspaceDescriptor project in projects.RecentProjects.Take(10))
        {
            string? projectPath = project.Path;
            if (projectPath is null)
            {
                continue;
            }

            var item = new ToolStripMenuItem(project.DisplayName)
            {
                Checked = projects.CurrentProject?.Path?.Equals(
                    projectPath,
                    StringComparison.OrdinalIgnoreCase) == true,
                ToolTipText = projectPath,
            };
            item.Click += (_, _) => ProjectSelected?.Invoke(projectPath);
            var remove = new ToolStripMenuItem(localizer["RemoveFromRecent"]);
            remove.Click += (_, _) => RemoveRecentProjectRequested?.Invoke(projectPath);
            item.DropDownItems.Add(remove);
            projectsMenu.DropDownItems.Add(item);
        }

        if (projectsMenu.DropDownItems.Count == 0)
        {
            projectsMenu.DropDownItems.Add(
                new ToolStripMenuItem(localizer["NoRecentProjects"]) { Enabled = false });
        }
        else
        {
            projectsMenu.DropDownItems.Add(new ToolStripSeparator());
            projectsMenu.DropDownItems.Add(
                localizer["ClearRecentProjects"],
                null,
                (_, _) => ClearRecentProjectsRequested?.Invoke());
        }

        menu.Items.Add(projectsMenu);
        menu.Items.Add(localizer["OpenCodexNow"], null, (_, _) => OpenCodexRequested?.Invoke());
        menu.Items.Add(localizer["Settings"], null, (_, _) => SettingsRequested?.Invoke());
        var startup = new ToolStripMenuItem(localizer["StartWithWindows"])
        {
            Checked = startWithWindows,
            CheckOnClick = true,
        };
        startup.CheckedChanged += (_, _) => StartWithWindowsChanged?.Invoke(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(localizer["Exit"], null, (_, _) => ExitRequested?.Invoke());
    }

}
