## Summary

## Testing

- [ ] `.\test.ps1`
- [ ] `.\verify-repository-safety.ps1`

## Safety

- [ ] No `auth.json` or credential material committed
- [ ] Main `.codex` and original app-data roots remain protected
- [ ] All writes use the centralized storage layout and protected-path policy
- [ ] Bootstrap code does not claim or perform real VS Code switching
