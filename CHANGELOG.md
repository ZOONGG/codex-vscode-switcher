# Changelog

## [1.0.0] - 2026-09-20

### Added

- End-to-end profile onboarding from the overlay, Settings, and Profile Manager: create an isolated `CODEX_HOME`, run the official Codex login in a separate terminal, validate the resulting profile, and optionally launch it immediately.
- Dynamic profile discovery and scrollable profile selection without a silent twelve-profile cutoff.
- Bundled `ZOONGG.codex-vscode-switcher-companion` extension for sanitized active-workspace tracking, automatic `chatgpt.openSidebar` invocation, a conflict-safe `Ctrl+Alt+C` entry point, and a compact `Codex` status-bar action.
- Global current-project restoration across account switches, a deduplicated ten-item recent-project list, first-launch project chooser, and explicit missing-project recovery actions.
- Shared ordinary VS Code extension installation files as the default, with an optional isolated extensions mode.
- Sanitized DNS/TCP/TLS/HTTPS/WebSocket/backend diagnostics, safe VS Code environment comparison, process-path reporting, and networking recovery actions.
- Confirmation-only allowlist proxy settings copy and path-only custom CA environment configuration.

- Explicit Settings preview visibility with live layout, orientation, width, scale, offset, and position updates.
- Sanitized structured activation diagnostics, log/diagnostic actions, and safe runtime-state reset.
- Optional allowlist-only VS Code customization import and a separate `Codex VS Code` desktop shortcut.
- Dedicated `--shared-data-dir` isolation for current VS Code builds.
- One-step first launch now detects VS Code through its PATH CLI shim, asks for the profile in-app, and offers an explicit install-and-continue action when the dedicated Codex extension is missing.
- Real isolated VS Code launch with dedicated data/extensions, child-only `CODEX_HOME`, workspace restoration, and official Codex extension detection/install.
- Transactional profile switching with graceful managed-only shutdown, verified-window success criteria, application-wide serialization, and one-shot rollback.
- Persisted sanitized managed-instance identity with PID/start-time/executable/command-line/process-tree/HWND verification.
- Event-driven overlay foreground, minimize, destroy, location, desktop, and direct-interaction visibility policy.
- Managed VS Code launch/restart, extension, workspace, timeout, status, and dedicated-directory controls in English and Russian.
- Fake process/window/profile/extension integration tests covering the managed isolation boundary.
- Independent Codex VS Code Switcher product identity and local migration branch.
- Central `CodexVsCodeStorageLayout` for app data, logs, backups, transactions, profile homes, dedicated VS Code data/extensions, and workspace metadata.
- Case-insensitive normalized protected-path policy for the main `.codex` root and original CodexProfileOverlay data.
- Core VS Code contracts, launch models, profile model, settings, and localized integration status.
- Explicit read-only legacy profile migration planner.
- Allowlist-only minimal backup service with 10 MB/file, 25 MB/transaction, five-backup, and 100 MB retention limits.
- Isolation, protected-path, managed activation, migration, backup, and product-identity tests.
- Persistent multi-monitor floating placement, background-only dragging, reset-position recovery, and corrupt-settings backup.
- Read-only process/path-based VS Code window attachment with floating fallback.
- Credential-safe valid / invalid / incomplete audit for copied profiles and runtime-data cleanup planning.

### Changed

- The overlay is non-topmost and defaults to visibility only with foreground managed VS Code.
- Dedicated VS Code and profile roots are enforced product paths rather than editable arbitrary directories.
- Profile selection now performs real managed VS Code activation instead of returning an unavailable placeholder result.
- Renamed solution, projects, namespaces, executable, startup entry, shortcuts, package names, and UI text to Codex VS Code Switcher.
- Moved all mutable application state to independent roots.
- Restored Compact, Expanded, and Auto controls in both floating and VS Code-attached overlay states.
- Profile activation controls are disabled for invalid and incomplete copied profiles.

### Fixed

- Managed Codex now uses VS Code's production application registry; only the local companion bridge is loaded as a development extension. Shared mode no longer overrides `--shared-data-dir`, so application-scoped Codex activates normally and profile chat history loads.
- “Add profile” now starts onboarding instead of reopening the already-visible Profile Manager.
- VS Code 1.131 application-scoped Codex installations are now loaded from the ordinary production registry while dedicated user data and profile-specific `CODEX_HOME` remain intact.
- The portable single-file EXE now embeds and atomically provisions its companion extension under application-owned local data, so copying only the EXE to the desktop still enables workspace reporting and automatic Codex sidebar opening.
- First-launch project selection now persists the project before resuming the original pending profile, keeps the profile ID across the modal picker, and offers Retry/recovery actions without restarting the switcher.
- Missing or delayed companion/sidebar readiness no longer prevents or invalidates a verified managed VS Code launch.
- Same-project ordinary VS Code windows remain outside managed identity and are covered by a two-window real smoke test.
- Project chooser and structured launch failures now use precise English/Russian project wording and stage-specific diagnostics.

- Managed VS Code now preserves the complete inherited parent environment while overriding only process-owned values such as `CODEX_HOME` and an explicitly selected CA path.
- Codex VS Code can use the exact ordinary Codex extension/backend executable path for VPN and per-application routing compatibility.

- Repeated real profile switches no longer fail when background activation progress crosses the WPF dispatcher boundary.
- Profile Pending/Active state and the switch lock now recover after success, failure, timeout, and cancellation.
- Launch failures now distinguish executable, access, shutdown, process-exit, no-window, and timeout causes.
- Settings no longer hides the overlay while the user edits its appearance.
- “Show switcher” now explains how to launch or focus managed VS Code instead of silently doing nothing while managed-only visibility is enabled.
- Managed launch now follows VS Code’s verified child-process handoff instead of failing immediately when the short-lived launcher exits before the editor window appears.

### Removed

- ChatGPT/Codex Desktop process control, window discovery, overlay attachment, shared authorization replacement, shared-state migration, and legacy rollback runtime.
- Original update endpoint and release links.

### Security

- Updated the bundled native SQLite library to the first non-vulnerable 2.x release for CVE-2025-6965 / GHSA-2m69-gcr7-jv3q while retaining the .NET 8 data-provider line.
- Ordinary VS Code processes are rejected unless they match the persisted root PID/start time, exact executable, exact dedicated arguments, and verified process tree.
- Active-profile state is committed only after a visible managed top-level window is verified.
- Normal switching never force-kills and never copies profile or authentication data.
- Production usage refresh no longer starts a separate Codex CLI process with `CODEX_HOME`; stored/manual indicators remain available.
- Recursive `CODEX_HOME` backup paths are unavailable.
- Protected roots and reparse-point paths fail before file operations.
- Existing reparse-point profile directories are rejected before incomplete-profile retry cleanup.
- Interactive window styling always removes `WS_EX_TRANSPARENT`; display-mode changes have no auth or process side effects.

[1.0.0]: https://github.com/ZOONGG/codex-vscode-switcher/releases/tag/v1.0.0
