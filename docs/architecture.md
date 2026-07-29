# Bootstrap architecture

Codex VS Code Switcher contains:

- `CodexVsCodeSwitcher.Core`: storage layout, protected-path policy, profile metadata, usage status, minimal backups, migration planning, and future VS Code contracts;
- `CodexVsCodeSwitcher`: floating/VS Code-attached WPF shell, tray lifecycle, hotkeys, settings, localization, and profile presentation;
- `CodexVsCodeSwitcher.Tests`: isolation and regression coverage with temporary dummy data.

## Trust boundaries

`CodexVsCodeStorageLayout` is the source of truth for every owned path. `ProtectedPathPolicy` rejects the main `.codex` state and original application data, descendants, traversal variants, case variants, alternate separators, and detectable reparse points.

Runtime storage services receive the policy before they create directories or write files. Profile management additionally confines paths to `.codex-vscode-profiles`.

## Bootstrap activation

`BootstrapProfileActivationService` validates only the profile identifier and returns `VsCodeSwitchingNotImplemented`. It has no filesystem or process dependencies. The WPF controller displays the localized result and never records a new active profile.

No ChatGPT/Codex Desktop process or window services are present.

## VS Code window boundary

Read-only window discovery is implemented for visible top-level windows owned by `Code.exe`, `Code - Insiders.exe`, or an exact configured executable. It does not use window titles, launch or stop processes, or accept unrelated Electron applications. If no supported window exists, the switcher remains in floating mode.

Launching VS Code, setting `CODEX_HOME`, reopening workspaces, and activating profiles remain future boundaries. `VsCodeLaunchOptions` keeps their future inputs in one model.

## Overlay state

`OverlaySettings` is the central persisted model for display mode, attached offsets, floating physical-pixel coordinates, monitor identity, scale, and integration preference. Settings writes use a same-directory temporary file and atomic replacement. A small corrupt settings file is renamed as a timestamped backup before safe defaults are loaded.

Floating placement is clamped against current work areas while preserving negative virtual-screen coordinates. Primary-button dragging starts only from non-interactive background regions and never invokes profile activation.

## Profile audit

`ProfileStorageAuditService` inspects immediate directories under `.codex-vscode-profiles`. It parses only the small top-level authentication JSON for structural validity. Runtime trees and files are counted and measured without content ingestion. Sessions, archived sessions, rollout JSONL, databases, logs, caches, attachments, and temporary data are ineligible for backup or activation.

## Minimal backups

`MinimalBackupService` accepts an explicit list of files. It never walks a profile tree. Only `auth.json` and `config.toml` are allowlisted, with strict size and retention caps. Its manifest stores file names, sizes, hashes, and timestamps—not credential contents.

## Updates

There is no configured update endpoint and no runtime network request to the original project.
