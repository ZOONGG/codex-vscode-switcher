# Privacy

Codex VS Code Switcher is local-only. It has no telemetry, analytics, cloud profile database, reverse proxy, background service, or configured update endpoint.

Non-secret application data and sanitized logs stay under `%LOCALAPPDATA%\CodexVsCodeSwitcher`. Dedicated profile homes stay under `%USERPROFILE%\.codex-vscode-profiles`.

Codex VS Code uses dedicated user-data, extensions, and shared-data directories. Optional setup import copies only confirmed editor settings, keybindings, snippets, named-profile preferences, and selected extension identifiers; it excludes account and storage databases.

The application does not display or log authentication contents. The main `.codex` directory and original application data are protected from reads used for copying and from all mutations.
