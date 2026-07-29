# Troubleshooting

## VS Code executable was not found

Open **Settings → VS Code integration** and select an exact `Code.exe` or `Code - Insiders.exe`. The dedicated directories are fixed and shown read-only.

## A profile does not appear

Only valid directories under `%USERPROFILE%\.codex-vscode-profiles` containing a top-level `auth.json` are listed. Automatic migration is intentionally disabled.

## The Codex extension is missing

Use **Install Codex extension**. Network access occurs only after that explicit action and targets the dedicated extension directory.

## The overlay is hidden

The default policy shows it only while a verified managed VS Code window is foreground. The tray remains available. Check that managed VS Code is running, restored, and focused.

## VS Code did not close

Save or discard changes in the managed VS Code window, then try again. The switcher does not force-close by default.

## A remembered workspace is missing

Choose another folder or `.code-workspace`, or explicitly open an empty window. The missing remembered path is not silently erased.

## Logs

Sanitized logs are stored under `%LOCALAPPDATA%\CodexVsCodeSwitcher\logs`.
