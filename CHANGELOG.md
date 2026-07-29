# Changelog

## Unreleased

### Added

- Independent Codex VS Code Switcher product identity and local migration branch.
- Central `CodexVsCodeStorageLayout` for app data, logs, backups, transactions, profile homes, dedicated VS Code data/extensions, and workspace metadata.
- Case-insensitive normalized protected-path policy for the main `.codex` root and original CodexProfileOverlay data.
- Bootstrap VS Code contracts, launch models, profile model, settings, and localized integration status.
- Explicit read-only legacy profile migration planner.
- Allowlist-only minimal backup service with 10 MB/file, 25 MB/transaction, five-backup, and 100 MB retention limits.
- Isolation, protected-path, bootstrap activation, migration, backup, and product-identity tests.
- Persistent multi-monitor floating placement, background-only dragging, reset-position recovery, and corrupt-settings backup.
- Read-only process/path-based VS Code window attachment with floating fallback.
- Credential-safe valid / invalid / incomplete audit for copied profiles and runtime-data cleanup planning.

### Changed

- Renamed solution, projects, namespaces, executable, startup entry, shortcuts, package names, and UI text to Codex VS Code Switcher.
- Moved all mutable application state to independent roots.
- Restored Compact, Expanded, and Auto controls in both floating and VS Code-attached overlay states.
- Profile activation controls are disabled for invalid and incomplete copied profiles.

### Removed

- ChatGPT/Codex Desktop process control, window discovery, overlay attachment, shared authorization replacement, shared-state migration, and legacy rollback runtime.
- Original update endpoint and release links.

### Security

- Profile selection now returns a localized not-implemented result without touching authentication data.
- Recursive `CODEX_HOME` backup paths are unavailable.
- Protected roots and reparse-point paths fail before file operations.
- Interactive window styling always removes `WS_EX_TRANSPARENT`; display-mode changes have no auth or process side effects.
