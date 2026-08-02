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

The legacy `%USERPROFILE%\.codex-profiles` source is read-only. Automatic migration is not executed.

## Credentials and backups

- Never commit, display, upload, or log real authentication contents.
- Tests use only dummy data in temporary directories.
- Backups accept only explicit top-level `auth.json` and `config.toml` inputs from the dedicated profile root.
- Maximum file size is 10 MB; maximum transaction size is 25 MB.
- Retention is limited to five completed backups and 100 MB.
- Sessions, rollout JSONL, databases, attachments, logs, and caches are forbidden.
- Recursive `CODEX_HOME` copying is not available.

## Runtime

The runtime launches and controls only the dedicated VS Code process tree it created. Root PID, process start time, exact executable path, exact `--user-data-dir` and `--shared-data-dir`, extension-mode-specific `--extensions-dir` presence, descendants, and top-level HWND ownership are revalidated before window attachment or shutdown.

`CODEX_HOME` is a child-process environment override. The application does not use `setx`, registry environment values, global user/machine environment mutation, or shared authentication replacement.

Normal switching uses `WM_CLOSE` and waits for VS Code to resolve unsaved work. It does not force-kill. Ordinary VS Code, ChatGPT Desktop, browsers, Explorer, and unrelated Electron applications are never targets.

Shared extension mode is default and read-only. The official Codex extension is installed only after explicit user action in isolated mode and only into the dedicated extension directory.

Network reports never include environment values, proxy credentials, authorization headers, cookies, command lines, or certificate contents. Optional custom CA configuration validates and passes a path only; TLS verification remains enabled. Proxy copying is allowlisted to `http.proxy`, `http.proxyStrictSSL`, and `http.proxySupport`, rejects embedded credentials, and requires confirmation.

Optional customization import uses an allowlist and explicit preview/confirmation. It excludes global/workspace storage, SQLite databases, credentials, sessions, cookies, machine identifiers, logs, and temporary files.

Automatic updates are disabled until a new update channel is explicitly configured.

Run `.\verify-repository-safety.ps1`, tests, and a Release build before publishing.

## Reporting

Report credential exposure privately. Never attach real authentication files, cookies, tokens, or screenshots containing credentials.
