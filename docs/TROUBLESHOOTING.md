# Troubleshooting

## Profile selection says the backend is not implemented

This is expected in the bootstrap build. No authentication file was changed.

## A profile does not appear

Only valid directories under `%USERPROFILE%\.codex-vscode-profiles` containing a top-level `auth.json` are listed. Automatic migration is intentionally disabled.

## The overlay is hidden

Use the tray icon or the configured global hotkey. Enable **Show switcher on start** in Settings.

## VS Code fields do not launch anything

The fields are prepared settings for the later backend. Detection, launch, switching, and attachment are not implemented yet.

## Logs

Sanitized logs are stored under `%LOCALAPPDATA%\CodexVsCodeSwitcher\logs`.
