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

### Changed

- Renamed solution, projects, namespaces, executable, startup entry, shortcuts, package names, and UI text to Codex VS Code Switcher.
- Moved all mutable application state to independent roots.
- Converted the overlay to a standalone bootstrap shell while preserving tray, hotkeys, themes, localization, profile UI, and usage indicators.

### Removed

- ChatGPT/Codex Desktop process control, window discovery, overlay attachment, shared authorization replacement, shared-state migration, and legacy rollback runtime.
- Original update endpoint and release links.

### Security

- Profile selection now returns a localized not-implemented result without touching authentication data.
- Recursive `CODEX_HOME` backup paths are unavailable.
- Protected roots and reparse-point paths fail before file operations.
