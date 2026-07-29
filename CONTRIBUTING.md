# Contributing

This is a Windows x64 .NET 8 WPF application. Keep changes local-only, telemetry-free, administrator-free, service-free, and credential-safe.

Keep managed VS Code switching process-scoped: exact dedicated arguments, child-only `CODEX_HOME`, verified PID/start-time/process-tree identity, and no ordinary VS Code targets.

Every filesystem feature must use `CodexVsCodeStorageLayout`, enforce `IProtectedPathPolicy`, reject reparse points where applicable, and use explicit file allowlists and size limits.

Before committing:

```powershell
.\verify-repository-safety.ps1
.\test.ps1
.\build.ps1
.\publish.ps1
```

Never add real authentication files or tests that depend on a user's login.
