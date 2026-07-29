# Bootstrap architecture

Codex VS Code Switcher contains:

- `CodexVsCodeSwitcher.Core`: storage layout, protected-path policy, profile metadata, usage status, minimal backups, migration planning, and future VS Code contracts;
- `CodexVsCodeSwitcher`: standalone WPF shell, tray lifecycle, hotkeys, settings, localization, and profile presentation;
- `CodexVsCodeSwitcher.Tests`: isolation and regression coverage with temporary dummy data.

## Trust boundaries

`CodexVsCodeStorageLayout` is the source of truth for every owned path. `ProtectedPathPolicy` rejects the main `.codex` state and original application data, descendants, traversal variants, case variants, alternate separators, and detectable reparse points.

Runtime storage services receive the policy before they create directories or write files. Profile management additionally confines paths to `.codex-vscode-profiles`.

## Bootstrap activation

`BootstrapProfileActivationService` validates only the profile identifier and returns `VsCodeSwitchingNotImplemented`. It has no filesystem or process dependencies. The WPF controller displays the localized result and never records a new active profile.

No ChatGPT/Codex Desktop process or window services are present.

## Future VS Code boundary

The core exposes interfaces for locating, launching, tracking processes/windows, resolving profile homes, and workspace history. No concrete VS Code backend exists yet.

`VsCodeLaunchOptions` keeps future executable, `--user-data-dir`, `--extensions-dir`, workspace, profile `CODEX_HOME`, and new-window intent in one model.

## Minimal backups

`MinimalBackupService` accepts an explicit list of files. It never walks a profile tree. Only `auth.json` and `config.toml` are allowlisted, with strict size and retention caps. Its manifest stores file names, sizes, hashes, and timestamps—not credential contents.

## Updates

There is no configured update endpoint and no runtime network request to the original project.
