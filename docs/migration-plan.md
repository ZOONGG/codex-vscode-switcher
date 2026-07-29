# Future explicit profile migration

This document is a design for a later stage. The bootstrap build does not copy profiles.

## Source and destination

- Read-only source: `%USERPROFILE%\.codex-profiles`
- Dedicated destination: `%USERPROFILE%\.codex-vscode-profiles`

The main `%USERPROFILE%\.codex` directory is never a migration source or destination.

## Current planner

`ProfileMigrationPlanner` enumerates only immediate profile directories that have valid names and are not reparse points. It lists only top-level:

- `auth.json`;
- `config.toml`.

Files above 10 MB and every other file are omitted. The planner creates no destination directories and changes no source data.

## Required future workflow

1. User opens migration explicitly.
2. The app displays detected profiles and allowed file names/sizes without reading or showing credential contents.
3. User selects profiles and confirms.
4. The protected-path policy validates source and destination again.
5. The app copies only the approved allowlist into a new dedicated profile directory.
6. The app verifies sizes and hashes.
7. A failure removes only the newly created destination; the source remains untouched.

Never copy sessions, archived sessions, rollout JSONL, databases, attachments, logs, caches, histories, projects, or an entire `CODEX_HOME`.
