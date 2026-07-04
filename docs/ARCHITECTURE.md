# Architecture

Codex Profile Overlay has two assemblies:

- `CodexProfileOverlay.Core`: token-safe services and models that can be unit-tested without WPF.
- `CodexProfileOverlay`: WPF shell, tray lifecycle, window attachment, hotkeys, settings/profile windows, and Codex process launching.

The switcher keeps the real workspace folders shared, but account-scoped Codex state is stored per profile. During a switch it saves `%USERPROFILE%\.codex\auth.json` plus the known Codex chat/session index files into `%USERPROFILE%\.codex-profiles\<profile>`, then restores the selected profile's saved state. Non-secret UI metadata lives under `%LOCALAPPDATA%\CodexProfileOverlay`.

Normal Codex launches do not set `CODEX_HOME`. The add-profile login flow sets `CODEX_HOME` only for that isolated `codex login` process.
