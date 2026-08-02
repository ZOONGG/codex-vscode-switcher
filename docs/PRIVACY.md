# Privacy

Codex VS Code Switcher is local-only. It has no telemetry, analytics, cloud profile database, reverse proxy, background service, or configured update endpoint.

Non-secret application data and sanitized logs stay under `%LOCALAPPDATA%\CodexVsCodeSwitcher`. Dedicated profile homes stay under `%USERPROFILE%\.codex-vscode-profiles`.

Codex VS Code always uses dedicated user-data and shared-data directories. By default only ordinary extension installation files are shared; accounts, settings, globalStorage, workspaceStorage, login state, cookies, databases, and profile state are not. Advanced mode uses a dedicated extensions directory. Optional setup import copies only confirmed editor settings, keybindings, snippets, named-profile preferences, and selected extension identifiers into dedicated storage; it excludes account and storage databases.

The application does not display or log authentication contents. The main `.codex` directory and original application data are protected from reads used for copying and from all mutations.
