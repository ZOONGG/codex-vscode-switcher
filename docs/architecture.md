# Managed VS Code architecture

## Components

- `CodexVsCodeSwitcher.Core`: launch plans, profile/workspace validation, switch transaction, rollback, managed metadata, extension management, protected paths, and testable visibility/identity policies.
- `CodexVsCodeSwitcher`: WPF shell, tray lifecycle, exact process launch, process-tree/window verification, Win32 event hooks, settings, and localization.
- `CodexVsCodeSwitcher.Tests`: fake profiles, fake processes/windows, fake launchers, temporary extension directories, and isolation regressions.
- `CodexVsCodeSwitcher.Companion`: bundled local VS Code extension for workspace observation and supported Codex commands.

## Trust boundaries

`CodexVsCodeStorageLayout` fixes the dedicated VS Code data, extension, and profile roots. `SettingsService` enforces those roots even when stale settings contain different values. `ProtectedPathPolicy` rejects the main `.codex` state and original application data, including traversal, case variants, and detectable reparse-point escapes.

Profile validation parses only a bounded top-level authentication JSON for structure. Values are not logged or persisted. Activation passes the profile directory directly as `CODEX_HOME`; it does not copy or merge any profile data.

The production controller uses the unavailable usage provider, so status UI remains independent but does not start a second Codex CLI process with another `CODEX_HOME`.

## Launch boundary

`VsCodeLaunchPlanBuilder` creates an argument list rather than a shell command. `ProcessCommandRunner` uses `UseShellExecute = false` and `ProcessStartInfo.ArgumentList`. Only the managed launch plan contains a `CODEX_HOME` environment override.

Every launch supplies the exact dedicated `--user-data-dir` and `--new-window`. Shared extension mode is the default and omits both `--extensions-dir` and `--shared-data-dir`, so application-scoped Codex remains enabled through VS Code's production registry. Isolated mode supplies the exact dedicated `--extensions-dir` and `--shared-data-dir`. Workspace input is either an existing folder, an existing `.code-workspace`, or null.

`ProcessStartInfo` retains the complete parent environment and applies only process-local application overrides. `CODEX_HOME` is always overridden. An explicitly configured existing CA path may additionally override only `CODEX_CA_CERTIFICATE` or `SSL_CERT_FILE`; values are never logged and no global environment is changed.

Managed launches pass only the application-owned companion as `--extensionDevelopmentPath`. The official `openai.chatgpt` package is deliberately not a development extension: VS Code loads it from the production application registry, which preserves its normal authentication and chat behavior. Four process-local `CODEX_VSCODE_SWITCHER_*` values carry the bridge directory, random session ID, automatic-open flag, and dedicated user-data path. These arguments and values are absent from ordinary VS Code launches.

## Workspace bridge

The companion observes `workspaceFolders` and `workspaceFile` at activation and after folder changes or window reload. It writes one bounded JSON message atomically under the dedicated bridge directory. The schema contains workspace type/path/folder paths, generated installation-scoped window ID, timestamp, official-extension presence, sidebar result, and shortcut-conflict boolean. It contains no source/file/chat/terminal contents, credentials, cookies, or process environment snapshot.

The desktop validates protocol/session, timestamp bounds, traversal, absolute path shape, protected roots, workspace type, `.code-workspace` suffix, and existence before atomically storing the current project. Temporary empty startup reports never erase a valid project. The history is global across account profiles, deduplicated case-insensitively, and capped at ten.

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

`CodexExtensionManager` scans the selected extension root for `openai.chatgpt-*` and reads only the extension version. Shared mode is read-only and never installs during startup or through the switcher. Explicit install uses the configured VS Code executable only in isolated mode. The official package is never passed as a development-extension path.

Network diagnostics use credential-free probes for DNS, TCP 443, TLS, HTTPS, secure WebSocket capability, system proxy detection, custom CA availability, and `codex.exe --version`. HTTP 401/403 proves network reachability and is reported as authentication after successful networking. Reports contain only allowlisted paths, hashes, process parentage/start times, setting names, environment variable names, and sanitized errors.

Dedicated VS Code settings are parsed with JSONC-compatible comments/trailing commas, updated atomically, and preserve unrelated keys.

The companion waits for `openai.chatgpt` activation and invokes `chatgpt.openSidebar` with 750 ms bounded retries for up to 20 seconds. Sidebar failure is reported independently and never rolls back a successful profile switch. `Ctrl+Alt+C` is a default extension contribution, so user keybindings retain priority; the dedicated keybindings file is inspected only to report a conflict. The status-bar action invokes the same official command.
