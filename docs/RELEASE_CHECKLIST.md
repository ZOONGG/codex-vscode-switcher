# Release checklist

- Confirm branch is `codex/fix-project-launch-after-picker`.
- Confirm no push or public release is planned.
- Run `.\verify-repository-safety.ps1`.
- Run `.\build-companion.ps1`.
- Run `.\test.ps1`.
- Run `.\build.ps1`.
- Run `.\publish.ps1`.
- Run `.\publish.ps1 -ArtifactLabel real-e2e-test` for the local handoff build.
- Run `.\smoke-test-vscode.ps1 -ExtensionMode All` and require two shared plus two isolated exact-instance cycles to pass.
- Run `.\smoke-test-workspace-restore.ps1` and require the same project in two distinct VS Code windows, remembered-project restore, simulated profile switch, `chatgpt.openSidebar`, and ordinary-VS-Code isolation to pass.
- Run the disposable credential-free network diagnostic and require the HTTPS stage to return a response.
- Verify the EXE, portable ZIP, and SHA256SUMS exist under `artifacts`.
- Follow `docs/manual-test-checklist.md` using only the managed VS Code instance.
- Confirm `.codex` and original application data timestamps did not change.
- Do not upload credentials, local settings, logs, backups, or profile homes.
