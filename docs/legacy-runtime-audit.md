# Legacy runtime audit

The copied baseline was audited before modification for product identifiers, process/window control, shared state, backup paths, startup registration, packaging, and old repository URLs.

| Baseline area | Relevant behavior | Bootstrap action |
|---|---|---|
| `AppPaths` | Pointed at `.codex`, `.codex-profiles`, and `CodexProfileOverlay` | Removed; replaced by `CodexVsCodeStorageLayout` |
| `AuthSwitchService`, atomic replacer | Replaced shared `auth.json` | Removed |
| `BackupMaintenanceService` | Created rollback backups for desktop switching | Removed; replaced by explicit allowlist-only minimal backups |
| `SharedCodexStateMigrationService` | Read and merged shared sessions/databases | Removed |
| `CodexProcessService` | Located, started, closed, and force-killed the desktop process | Removed |
| `CodexWindowFinder` / `CodexWindowInfo` | Matched desktop process/window metadata | Removed |
| `OverlayController` | Attached to and tracked a Codex Desktop window | Replaced with isolated floating state and read-only VS Code window tracking |
| Settings UI | Exposed launch, close, and desktop attachment behavior | Replaced with VS Code attachment controls while launch/profile activation remain disabled |
| Mutex, startup value, executable, AppUserModelID | Could collide with the original application | Replaced with unique identifiers |
| Install/publish/CI | Used old executable and archive names | Renamed; new installer AppId added |
| Repository/release URLs | Pointed at the copied public project | Removed; update channel is unconfigured |

The retained `CodexCliStatusUsageProvider` runs only after an explicit status request (or opt-in automatic refresh) with `CODEX_HOME` set to a dedicated profile directory. Its `Process.Start` launches only its own `codex app-server` child, and its `Kill` fallback terminates only that child tree on cancellation/timeout. It does not control a desktop window or modify the main `.codex` root.

The only other shell launch opens an application-owned folder in Explorer after protected-path validation. No ChatGPT/Codex Desktop or VS Code process is launched in the bootstrap build.
