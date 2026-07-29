# Security Policy

Codex VS Code Switcher is local-only and must never modify the user's main Codex/ChatGPT Desktop state or the original Codex Swap Account application state.

## Protected roots

All reads used as copy sources and every write, replace, move, delete, or backup operation must reject these roots and their descendants:

```text
%USERPROFILE%\.codex
%LOCALAPPDATA%\CodexProfileOverlay
```

Paths are normalized, compared case-insensitively on Windows, checked after `..` resolution and separator normalization, and rejected when an existing path segment is a reparse point.

## Dedicated data

Mutable application state belongs under `%LOCALAPPDATA%\CodexVsCodeSwitcher`. Dedicated profile homes belong under `%USERPROFILE%\.codex-vscode-profiles`.

The legacy `%USERPROFILE%\.codex-profiles` source is read-only. Migration is not executed in this bootstrap build.

## Credentials and backups

- Never commit, display, upload, or log real authentication contents.
- Tests use only dummy data in temporary directories.
- Backups accept only explicit top-level `auth.json` and `config.toml` inputs from the dedicated profile root.
- Maximum file size is 10 MB; maximum transaction size is 25 MB.
- Retention is limited to five completed backups and 100 MB.
- Sessions, rollout JSONL, databases, attachments, logs, and caches are forbidden.
- Recursive `CODEX_HOME` copying is not available.

## Runtime

The bootstrap runtime does not locate, launch, close, kill, restart, or attach to ChatGPT/Codex Desktop or VS Code. Selecting a profile fails safely without filesystem mutation.

Automatic updates are disabled until a new update channel is explicitly configured.

Run `.\verify-repository-safety.ps1`, tests, and a Release build before publishing.

## Reporting

Report credential exposure privately. Never attach real authentication files, cookies, tokens, or screenshots containing credentials.
