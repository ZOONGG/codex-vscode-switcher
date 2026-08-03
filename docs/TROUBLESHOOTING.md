# Troubleshooting

## VS Code executable was not found

Open **Settings → VS Code integration** and select an exact `Code.exe` or `Code - Insiders.exe`. The dedicated directories are fixed and shown read-only.

## A profile does not appear

Only valid directories under `%USERPROFILE%\.codex-vscode-profiles` containing a top-level `auth.json` are listed. Automatic migration is intentionally disabled.

## The Codex extension is missing

With **Use my existing VS Code extensions** enabled, install Codex in ordinary VS Code; the switcher never changes ordinary extensions. In advanced isolated mode, use **Install Codex extension** after confirmation.

## Chats keep loading or prompts do not complete

Keep **Use my existing VS Code extensions** enabled and restart Codex VS Code. This makes ordinary and managed VS Code use the same Codex extension/backend executable path while user data and `CODEX_HOME` remain isolated.

Run **Test Codex connection** to see separate DNS, TCP, TLS, HTTPS, WebSocket, proxy, certificate, and backend results. HTTP 401/403 means the server was reached. Use **Compare regular and Codex VS Code** to copy a sanitized path/hash comparison for VPN per-application routing. Never disable certificate verification or add `--ignore-certificate-errors`.

## The overlay is hidden

The default policy shows it only while a verified managed VS Code window is foreground. The tray remains available. Check that managed VS Code is running, restored, and focused.

While Settings is open, an explicit floating preview remains visible even without Codex VS Code. If it persists after Settings closes, reopen and close Settings once, then use **Reset Codex VS Code runtime state**.

## VS Code did not close

Save or discard changes in the managed VS Code window, then try again. The switcher does not force-close by default.

## A remembered workspace is missing

Choose another folder or `.code-workspace`, or explicitly open an empty window. The missing remembered path is not silently erased.

## VS Code launched but the companion is delayed

The managed window remains a successful launch. Use **Open Codex now** or wait for the project report. Companion/sidebar warnings never close the managed window, roll back the selected profile, or erase the selected project.

## Logs

Sanitized logs are stored under `%LOCALAPPDATA%\CodexVsCodeSwitcher\logs`.

Use **Copy diagnostics** for the allowlisted category, paths, profile/workspace, PID/start time, timeout stage, and sanitized exception only. If a launch remains stale, close the exact Codex VS Code instance and use **Reset Codex VS Code runtime state**; this does not remove profiles, credentials, settings, workspaces, or extensions.
