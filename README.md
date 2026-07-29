# Codex VS Code Switcher

Codex VS Code Switcher is a new, independent Windows application being prepared to switch isolated Codex profiles inside a dedicated Visual Studio Code instance.

This repository is currently at the **bootstrap and isolation stage**. The storage boundaries, product identity, reusable WPF shell, settings, and future integration contracts are ready. VS Code discovery, launching, profile activation, workspace reopening, window tracking, and overlay attachment are not implemented yet.

Selecting a profile is intentionally fail-safe and displays:

> VS Code profile switching is not implemented in this bootstrap build.

No authentication file is changed.

## Isolation guarantees

The application uses only its own mutable roots:

```text
%LOCALAPPDATA%\CodexVsCodeSwitcher
├── settings.json
├── profiles.json
├── profile-status.json
├── logs
├── backups
├── transactions
├── VSCodeData
├── VSCodeExtensions
└── last-workspace.json

%USERPROFILE%\.codex-vscode-profiles
```

The following roots are protected in code. Reads used for copying and all writes, moves, deletes, replacements, and backup operations are rejected before filesystem mutation:

```text
%USERPROFILE%\.codex
%LOCALAPPDATA%\CodexProfileOverlay
```

The original Codex/ChatGPT desktop state and the original Codex Swap Account application are outside this product's storage boundary.

## What works in this build

- independent executable, assembly, namespace, mutex, AppUserModelID, startup entry, shortcuts, installer identity, and app-data roots;
- WPF overlay shell, tray icon, global hotkeys, themes, English/Russian localization, settings, and safe profile metadata management;
- dedicated VS Code integration settings and placeholders;
- profile status and usage indicator UI for profiles already present under the dedicated profile root;
- explicit allowlist-only minimal backup infrastructure with 10 MB/file, 25 MB/transaction, five-backup, and 100 MB retention limits;
- read-only planning for a future explicit migration from `.codex-profiles`;
- local builds, tests, safety scanning, and portable publishing.

## Deliberately disabled

- locating or launching VS Code;
- setting `CODEX_HOME` for VS Code;
- switching a VS Code profile;
- attaching the overlay to a VS Code window;
- controlling, closing, or restarting ChatGPT/Codex Desktop;
- reading or replacing `%USERPROFILE%\.codex\auth.json`;
- recursive `CODEX_HOME` backups;
- automatic profile migration;
- automatic updates.

The update channel is not configured. The application does not contact the original project's release endpoint.

## Future profile migration

The source `%USERPROFILE%\.codex-profiles` is read-only. The current build can create a plan that lists only top-level `auth.json` and `config.toml` files smaller than 10 MB. It does not execute a migration.

A later explicit workflow must require confirmation, preserve the source, and exclude sessions, rollout JSONL files, databases, logs, caches, and attachments.

## Build and test

Requirements: Windows 10/11 x64, PowerShell, and .NET 8 SDK. A repository-local SDK is used when available.

```powershell
.\test.ps1
.\build.ps1
.\verify-repository-safety.ps1
.\publish.ps1
```

Published files:

```text
artifacts\publish\CodexVsCodeSwitcher.exe
artifacts\CodexVsCodeSwitcher-win-x64-portable.zip
artifacts\SHA256SUMS.txt
```

## Repository structure

```text
src\CodexVsCodeSwitcher          WPF shell
src\CodexVsCodeSwitcher.Core     storage, safety, models, contracts
tests\CodexVsCodeSwitcher.Tests  isolation and regression tests
docs\architecture.md             bootstrap architecture
docs\migration-plan.md           future explicit migration design
docs\legacy-runtime-audit.md      removed legacy behavior audit
```

## Security

The application is local-only, telemetry-free, and does not log raw authentication contents. Never publish or commit real `auth.json` files. See [SECURITY.md](SECURITY.md).

## Status

This build is preparation work, not a functional VS Code account switcher. See [CHANGELOG.md](CHANGELOG.md) for the current scope.

## License

MIT. OpenAI, Codex, ChatGPT, Microsoft, and Visual Studio Code are trademarks of their respective owners. This independent community tool is not affiliated with or endorsed by OpenAI or Microsoft.
