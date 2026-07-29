# Managed VS Code architecture

## Components

- `CodexVsCodeSwitcher.Core`: launch plans, profile/workspace validation, switch transaction, rollback, managed metadata, extension management, protected paths, and testable visibility/identity policies.
- `CodexVsCodeSwitcher`: WPF shell, tray lifecycle, exact process launch, process-tree/window verification, Win32 event hooks, settings, and localization.
- `CodexVsCodeSwitcher.Tests`: fake profiles, fake processes/windows, fake launchers, temporary extension directories, and isolation regressions.

## Trust boundaries

`CodexVsCodeStorageLayout` fixes the dedicated VS Code data, extension, and profile roots. `SettingsService` enforces those roots even when stale settings contain different values. `ProtectedPathPolicy` rejects the main `.codex` state and original application data, including traversal, case variants, and detectable reparse-point escapes.

Profile validation parses only a bounded top-level authentication JSON for structure. Values are not logged or persisted. Activation passes the profile directory directly as `CODEX_HOME`; it does not copy or merge any profile data.

The production controller uses the unavailable usage provider, so status UI remains independent but does not start a second Codex CLI process with another `CODEX_HOME`.

## Launch boundary

`VsCodeLaunchPlanBuilder` creates an argument list rather than a shell command. `ProcessCommandRunner` uses `UseShellExecute = false` and `ProcessStartInfo.ArgumentList`. Only the managed launch plan contains a `CODEX_HOME` environment override.

Every launch supplies the exact dedicated `--user-data-dir`, `--extensions-dir`, and `--new-window`. Workspace input is either an existing folder, an existing `.code-workspace`, or null.

## Managed identity

`managed-vscode.json` stores only root PID/start time, profile ID, workspace, executable path, dedicated paths, last verified HWND, and launch time.

`ManagedVsCodeRuntime` rejects stale state unless the live root PID, start time, executable, and exact dedicated command-line arguments match. Toolhelp process snapshots establish descendants. Only visible top-level windows from the verified set are candidates; the foreground candidate is preferred.

## Switch transaction

`ProfileActivationService` uses an application-wide semaphore. It validates all inputs, requests normal `WM_CLOSE` on verified managed top-level windows, waits for exit, launches the new process, and waits for a verified window. Active profile and managed metadata writes are atomic and occur only after window verification.

If the new launch fails after a previous managed instance closed, one rollback launch uses the previous profile and workspace. There is no recursive retry.

## Overlay visibility

`ManagedVsCodeWindowTracker` registers out-of-context hooks for foreground, minimize start/end, destroy, show, hide, and location change. Callbacks marshal to the WPF dispatcher. A two-second timer reconciles missed events.

`ManagedOverlayVisibilityPolicy` requires a valid, visible, non-minimized managed window and either managed foreground or direct overlay interaction. Secure/lock desktops always hide the overlay. The WPF window is non-topmost; native placement uses `SWP_NOZORDER | SWP_NOACTIVATE`.

## Extension and settings

`CodexExtensionManager` scans only the dedicated extension root for `openai.chatgpt-*`, reads only the extension version, and never installs during startup. Explicit install uses the configured VS Code executable with both dedicated path arguments.

Dedicated VS Code settings are parsed with JSONC-compatible comments/trailing commas, updated atomically, and preserve unrelated keys.
