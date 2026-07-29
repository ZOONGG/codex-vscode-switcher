# Overlay controls repair

## Root cause

The bootstrap split replaced the original window-tracking loop with a standalone `ShowOverlay` implementation, but retained `OverlayWindow` almost unchanged. Display-mode resolution, position presets, and dragging still ran only from `UpdatePlacement(ownerHwnd)`. The bootstrap controller never called `AttachTo` or `UpdatePlacement`, so `ownerHwnd` stayed zero, mouse movement returned immediately, and `currentMode` stayed `Expanded`. `ShowOverlay` also overwrote the window coordinates with a top-right default every time.

The controls themselves used direct WPF event handlers rather than commands or a view model. Their click handlers, selection events, `IsEnabled` values, localization keys, dispatcher access, and settings service registration were working. `WS_EX_TRANSPARENT` was not applied. The failure was the broken controller/window state path, not DataContext, CanExecute, hit testing, dependency injection, localization, or protected-path rejection.

## Repair

- Floating placement is now a first-class state with physical-pixel coordinates and monitor identity.
- Compact, Expanded, and Auto resolve in both floating and attached modes.
- Background-only primary-button dragging uses native screen coordinates, supports negative coordinates and mixed monitor layouts, and always releases capture.
- Interactive elements and profile items do not start window dragging.
- Saved off-screen positions are clamped to a visible work area; reset uses the primary monitor.
- Read-only VS Code window discovery accepts only `Code.exe`, `Code - Insiders.exe`, or an exact configured executable path.
- Interactive window styling explicitly removes `WS_EX_TRANSPARENT`.
- Profile activation remains fail-safe and disabled.
