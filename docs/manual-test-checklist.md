# Local manual-test checklist

Use the local portable build only after automated verification completes. Do not run this checklist from the VS Code window used to build the switcher.

1. Start `artifacts\publish\CodexVsCodeSwitcher.exe`.
2. Open Settings and confirm the overlay remains visible and interactive.
3. Change scale, compact/expanded widths, Auto/Compact/Expanded, orientation, position, and offsets; confirm every change appears live.
4. Reset position, close Settings, focus ChatGPT/Explorer, and confirm the overlay immediately returns to Codex-VS-Code-only visibility.
5. Select profile A and launch Codex VS Code.
6. Confirm the expected account manually in the Codex sidebar.
7. Open a test repository, then click profile B directly in the overlay.
8. Confirm only Codex VS Code restarts and the same repository returns.
9. Click profile C, then switch back to profile A.
10. Cause one harmless failure (for example, cancel an unsaved-file close) and confirm profile buttons never remain disabled.
11. Confirm ordinary VS Code remains open and unaffected.
12. Confirm ChatGPT Desktop remains on the main account.
13. Test **Import my VS Code setup**; inspect the preview before confirming and verify selected customizations appear only in Codex VS Code.
14. Close Codex VS Code deliberately, use **Reset Codex VS Code runtime state**, and confirm profiles/settings/extensions remain.
15. Optionally create the **Codex VS Code** shortcut and confirm it launches the last profile or chooser without replacing the ordinary VS Code shortcut.

Do not include account emails, authentication values, tokens, session identifiers, or screenshots containing credentials in test notes.
