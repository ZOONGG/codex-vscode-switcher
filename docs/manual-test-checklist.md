# Local manual-test checklist

Use the local portable build only after automated verification completes. Do not run this checklist from the VS Code window used to build the switcher.

1. Start `artifacts\publish\CodexVsCodeSwitcher.exe`.
2. Open Settings and confirm the overlay remains visible and interactive.
3. Change scale, compact/expanded widths, Auto/Compact/Expanded, orientation, position, and offsets; confirm every change appears live.
4. Reset position, close Settings, focus ChatGPT/Explorer, and confirm the overlay immediately returns to Codex-VS-Code-only visibility.
5. Select profile A and launch Codex VS Code.
6. Confirm the expected account manually in the Codex sidebar.
7. Open a test repository, then click profile B directly in the overlay.
8. Confirm only Codex VS Code restarts, the exact same repository returns, and the Codex sidebar opens with its input ready.
9. Click profile C, then switch back to profile A.
10. Cause one harmless failure (for example, cancel an unsaved-file close) and confirm profile buttons never remain disabled.
11. Confirm ordinary VS Code remains open and unaffected.
12. Confirm ChatGPT Desktop remains on the main account.
13. Test **Import my VS Code setup**; inspect the preview before confirming and verify selected customizations appear only in Codex VS Code.
14. Close Codex VS Code deliberately, use **Reset Codex VS Code runtime state**, and confirm profiles/settings/extensions remain.
15. Optionally create the **Codex VS Code** shortcut and confirm it launches the last profile or chooser without replacing the ordinary VS Code shortcut.
16. Open a different folder through **File → Open Folder**, wait briefly, and confirm it appears as **Current project** in Settings and the tray without selecting it in Switcher.
17. Confirm `Ctrl+Alt+C` and the status-bar **Codex** item reopen/focus the sidebar. If the shortcut conflicts, confirm the existing binding was not overwritten.

## Codex networking checklist

1. Enable **Использовать мои расширения VS Code**.
2. Restart Codex VS Code.
3. Confirm the usual extensions appear.
4. Open the Codex panel.
5. Confirm chats finish loading.
6. Send a harmless test message.
7. Switch to a second profile.
8. Confirm Codex loads under the second profile.
9. Test all four profiles.
10. Run **Проверить подключение Codex** if loading fails.

Do not include account emails, authentication values, tokens, session identifiers, or screenshots containing credentials in test notes.
