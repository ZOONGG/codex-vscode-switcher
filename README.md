# Codex VS Code Switcher

Codex VS Code Switcher is an independent Windows application for running one isolated, managed Visual Studio Code environment with selectable Codex profiles.

It does not switch ChatGPT Desktop, does not replace a shared authentication file, and does not control ordinary VS Code windows.

## How isolation works

The managed instance always launches with:

```text
Code.exe
  --user-data-dir "%LOCALAPPDATA%\CodexVsCodeSwitcher\VSCodeData"
  --shared-data-dir "%LOCALAPPDATA%\CodexVsCodeSwitcher\VSCodeSharedData"
  --new-window
  [optional folder or .code-workspace]
```

By default, **Use my existing VS Code extensions** is enabled, so no
`--extensions-dir` argument is passed and VS Code uses `%USERPROFILE%\.vscode\extensions`.
Only extension installation files are shared; user data, settings, storage, cookies,
login state, and `CODEX_HOME` remain isolated. Advanced isolated mode passes
`--extensions-dir "%LOCALAPPDATA%\CodexVsCodeSwitcher\VSCodeExtensions"`.

`CODEX_HOME` is added only to `ProcessStartInfo.Environment` for that managed launch and points directly to the selected directory under `%USERPROFILE%\.codex-vscode-profiles`. The switcher never copies a profile or replaces `auth.json`.

Mutable application state remains under:

```text
%LOCALAPPDATA%\CodexVsCodeSwitcher
%USERPROFILE%\.codex-vscode-profiles
```

The following roots remain protected:

```text
%USERPROFILE%\.codex
%LOCALAPPDATA%\CodexProfileOverlay
```

## Managed-instance identity

A window is managed only when all of the following remain valid:

- the persisted root PID and process start time match;
- the executable path exactly matches the configured/detected VS Code executable;
- the root command line contains the exact dedicated `--user-data-dir` and `--shared-data-dir`, plus either no `--extensions-dir` in shared mode or the exact dedicated value in isolated mode;
- the window belongs to the launched root process or a verified descendant;
- the HWND is a visible top-level window owned by that verified process tree.

A process named `Code.exe` is not sufficient. Window titles are not used as identity.

## Overlay behavior

The overlay is not globally topmost. Foreground, minimize/restore, destroy, show/hide, and location changes are tracked with Win32 event hooks, plus a two-second defensive reconciliation timer.

By default the overlay:

- appears only while a verified managed VS Code window is foreground;
- remains usable while its own controls and menus are being used;
- hides over ChatGPT, browsers, Explorer, ordinary VS Code, other editors, Task View, full-screen unrelated apps, and secure desktops;
- hides when the managed window is minimized or closed;
- returns when a managed window is restored and focused.

Auto, Compact, Expanded, dragging, scale, offsets, saved position, DPI, multi-monitor placement, and reset-position remain available.

Opening Settings creates an explicit interactive preview lease. Layout, orientation, widths, scale, offsets, and position update live even when Codex VS Code is not running. Closing Settings immediately restores managed-window-only visibility; preview never attaches to the Settings window and never launches a profile.

## Profile switching

Profile switches are serialized and transactional:

1. validate the profile, executable, dedicated paths, extension, and workspace;
2. send normal close requests only to verified managed VS Code windows;
3. wait for VS Code and any unsaved-file confirmation;
4. launch the selected profile with isolated arguments and process-only `CODEX_HOME`;
5. require a valid managed process and visible top-level window;
6. persist the active profile only after verification;
7. relaunch the previous profile once if the new launch fails after shutdown.

Force-close is not used by the normal workflow.

Clicking a profile starts this flow directly. The clicked profile is Pending until its new window is verified, then becomes Active. Success, failure, timeout, and cancellation all release the switch lock and re-enable profile controls.

On first launch, **Which project should Codex open?** lets the user choose a project folder, a `.code-workspace` project file, or open without a project. The chosen project is stored atomically before the original pending profile activation resumes; a failed launch keeps that choice available for Retry.

## Codex extension and workspaces

The managed environment detects the official Marketplace extension `openai.chatgpt` in the active extension directory. Shared mode never installs, updates, removes, enables, disables, or modifies ordinary extensions. Installation occurs only after an explicit action in isolated mode and targets only the dedicated extension directory.

Settings includes sanitized DNS/TCP/TLS/HTTPS/WebSocket/backend diagnostics, a safe
regular-vs-managed comparison, an allowlist-only proxy settings copy, optional
path-only `CODEX_CA_CERTIFICATE`/`SSL_CERT_FILE` configuration, Codex log access,
extension-host restart, and a reset limited to the switcher's diagnostic cache.

The dedicated `User\settings.json` is updated atomically while preserving unrelated valid settings. `chatgpt.openOnStartup` is configured only there.

The switcher bundles a lightweight companion extension only for the dedicated Codex VS Code launch. It receives an application-owned bridge path through process-local environment variables, reports only sanitized workspace/UI state through atomic JSON under `%LOCALAPPDATA%\CodexVsCodeSwitcher\bridge`, and never opens a network port. Ordinary VS Code is not installed or launched with this companion.

The companion files are embedded in the portable EXE and atomically provisioned into `%LOCALAPPDATA%\CodexVsCodeSwitcher\companion-extension`. The EXE therefore remains fully functional when copied by itself; the adjacent `companion-extension` publish folder is only a transparent packaging fallback.

Opening a folder or `.code-workspace` inside Codex VS Code automatically updates one global current project shared by all Codex account profiles. The current project reopens after profile switches and restarts. Up to ten deduplicated recent projects are available in Settings and the tray; missing projects remain visible until the user chooses another folder, opens an empty window, or removes the entry.

After a verified managed window appears, the companion activates the official extension and invokes `chatgpt.openSidebar` with bounded retries. **Open Codex automatically** is enabled by default. `Ctrl+Alt+C` and the compact status-bar item **Codex** reopen and focus the sidebar; an existing dedicated keybinding is reported and left unchanged.

Managed-window verification is the launch success boundary. A missing/delayed companion report or a failed sidebar command produces a separate warning and never turns an already verified Codex VS Code window into a launch failure.

**Import my VS Code setup** is optional and confirmation-only. Its preview lists the allowed settings, keybindings, snippets, named-profile files, and extension count. It never copies storage databases, sessions, credentials, cookies, machine identifiers, logs, or temporary files. Chosen extension IDs are installed separately into the dedicated extensions directory.

**Reset Codex VS Code runtime state** clears only stale PID/start-time/HWND metadata after verifying that no exact managed instance is running. It preserves profiles, credentials, workspaces, settings, and extensions. **Create Codex VS Code shortcut** adds a separate desktop shortcut and does not replace the ordinary Visual Studio Code shortcut.

## Build and test

Requirements: Windows 10/11 x64, PowerShell, and .NET 8 SDK. A repository-local SDK is used when available.

```powershell
.\test.ps1
.\build-companion.ps1
.\build.ps1
.\verify-repository-safety.ps1
.\publish.ps1
.\publish.ps1 -ArtifactLabel real-e2e-test
.\smoke-test-workspace-restore.ps1
```

Published files:

```text
artifacts\publish\CodexVsCodeSwitcher.exe
artifacts\CodexVsCodeSwitcher-win-x64-portable.zip
artifacts\SHA256SUMS.txt
```

See [manual-test-checklist.md](docs/manual-test-checklist.md) before using real profiles.

## Security

The application is local-only, telemetry-free, and never logs authentication contents or raw process environments. Never publish or commit real `auth.json` files. See [SECURITY.md](SECURITY.md).

## License

MIT. OpenAI, Codex, ChatGPT, Microsoft, and Visual Studio Code are trademarks of their respective owners. This independent community tool is not affiliated with or endorsed by OpenAI or Microsoft.
