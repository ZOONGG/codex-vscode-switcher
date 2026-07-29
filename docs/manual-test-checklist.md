# Local manual-test checklist

Use the local portable build only after automated verification completes. Do not run this checklist from the VS Code window used to build the switcher.

1. Close the development VS Code window or continue from a different ordinary VS Code window.
2. Launch `artifacts\publish\CodexVsCodeSwitcher.exe`.
3. Open Settings and confirm the managed status is **not running**.
4. If needed, use **Install Codex extension** and confirm a version is detected in the dedicated environment.
5. Choose one real profile.
6. Launch managed VS Code.
7. Confirm the Codex sidebar opens under the expected account.
8. Open a test repository and remember its path.
9. Switch to a second profile.
10. Confirm only managed VS Code restarts.
11. Confirm the same repository reopens.
12. Confirm ChatGPT Desktop remains on the main account.
13. Confirm every ordinary VS Code window remains open.
14. Focus ChatGPT and Explorer; confirm the overlay disappears immediately.
15. Focus managed VS Code; confirm the overlay returns.
16. Minimize managed VS Code; confirm the overlay hides. Restore and focus it; confirm the overlay returns.
17. Test Auto, Compact, Expanded, drag, scale, offsets, and reset position.
18. Open overlay menus and click its controls; confirm it does not flicker or disappear.
19. Test a folder workspace, a `.code-workspace` file, and an explicitly empty managed window.
20. With a disposable unsaved file, start a switch and confirm VS Code can block shutdown until you save or discard.
21. Switch through all four profiles.

Do not include account emails, authentication values, tokens, session identifiers, or screenshots containing credentials in test notes.
