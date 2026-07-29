# Changelog

## Unreleased

### Added

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
- Bootstrap VS Code contracts, launch models, profile model, settings, and localized integration status.
- Explicit read-only legacy profile migration planner.
- Allowlist-only minimal backup service with 10 MB/file, 25 MB/transaction, five-backup, and 100 MB retention limits.
- Isolation, protected-path, managed activation, migration, backup, and product-identity tests.
- Persistent multi-monitor floating placement, background-only dragging, reset-position recovery, and corrupt-settings backup.
- Read-only process/path-based VS Code window attachment with floating fallback.
- Credential-safe valid / invalid / incomplete audit for copied profiles and runtime-data cleanup planning.

### Changed

- The overlay is non-topmost and defaults to visibility only with foreground managed VS Code.
- Dedicated VS Code and profile roots are enforced product paths rather than editable arbitrary directories.
- Profile selection now performs real managed VS Code activation instead of returning the bootstrap unavailable result.
- Renamed solution, projects, namespaces, executable, startup entry, shortcuts, package names, and UI text to Codex VS Code Switcher.
- Moved all mutable application state to independent roots.
- Restored Compact, Expanded, and Auto controls in both floating and VS Code-attached overlay states.
- Profile activation controls are disabled for invalid and incomplete copied profiles.

### Fixed

- “Show switcher” now explains how to launch or focus managed VS Code instead of silently doing nothing while managed-only visibility is enabled.

### Removed

- ChatGPT/Codex Desktop process control, window discovery, overlay attachment, shared authorization replacement, shared-state migration, and legacy rollback runtime.
- Original update endpoint and release links.

### Security

- Ordinary VS Code processes are rejected unless they match the persisted root PID/start time, exact executable, exact dedicated arguments, and verified process tree.
- Active-profile state is committed only after a visible managed top-level window is verified.
- Normal switching never force-kills and never copies profile or authentication data.
- Production usage refresh no longer starts a separate Codex CLI process with `CODEX_HOME`; stored/manual indicators remain available.
- Profile selection now returns a localized not-implemented result without touching authentication data.
- Recursive `CODEX_HOME` backup paths are unavailable.
- Protected roots and reparse-point paths fail before file operations.
- Interactive window styling always removes `WS_EX_TRANSPARENT`; display-mode changes have no auth or process side effects.
