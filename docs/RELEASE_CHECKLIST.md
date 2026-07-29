# Release checklist

- Confirm branch is `migration/vscode-bootstrap` and `git remote -v` is empty.
- Run `.\verify-repository-safety.ps1`.
- Run `.\test.ps1`.
- Run `.\build.ps1`.
- Run `.\publish.ps1`.
- Verify the EXE, portable ZIP, and SHA256SUMS exist under `artifacts`.
- Launch the published executable and verify tray, overlay, Settings, English/Russian text, and safe profile-selection failure.
- Confirm `.codex` and original application data timestamps did not change.
- Do not upload credentials, local settings, logs, backups, or profile homes.
