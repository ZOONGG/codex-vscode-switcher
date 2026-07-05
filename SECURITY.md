# Security Policy

Codex Profile Overlay is local-only and must never upload, display, parse, or log `auth.json`.

## Credential Rules

- Never commit `auth.json`, `.codex-profiles`, backups, logs, or local app data.
- Tests must use temporary directories and dummy `auth.json` contents only.
- Profile metadata in `%LOCALAPPDATA%\CodexProfileOverlay\profiles.json` may contain display names, order, initials, and local accent choices only.
- Manual status metadata and cached normalized usage snapshots belong only in `%LOCALAPPDATA%\CodexProfileOverlay\profile-status.json`, never beside profile credentials.
- The experimental automatic usage provider may use only the official interactive Codex CLI `/status` command through an isolated hidden ConPTY session with profile-specific `CODEX_HOME`.
- Usage providers must discard raw terminal output immediately after parsing recognized limit rows. Undocumented endpoints, browser scraping/cookies, raw responses, and credential parsing are prohibited.
- Cached usage snapshots may contain only limit labels, remaining percentages, reset timestamps, capture timestamp, Codex CLI version, and the sanitized source identifier `codex-cli-status`.
- Provider logs may contain only an internal profile identifier, timestamps, capability/result category, sanitized error category, and snapshot age. They must never contain account email, session ID, model, permissions, directory, project path, or auth contents.
- Logs must stay token-safe and must not include raw exception stacks for normal user flows.
- Run `.\verify-repository-safety.ps1` before staging or publishing changes.

## Reporting

Please report credential exposure privately. Do not attach real `auth.json` files, screenshots containing tokens, browser cookies, or account credentials.
