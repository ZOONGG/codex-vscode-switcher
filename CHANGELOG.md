# Changelog

## Unreleased

- Added profile status metadata for manual tracking of profile limits and availability.
- Added automatic limit indicators in the overlay (⭐ recommended, 🟢 high capacity, 🟡 medium capacity, 🔴 low capacity).
- Added profile recommendation algorithm based on usage data.
- Added status and limits settings panel with configurable thresholds.
- Added IUsageProvider abstraction for future Codex usage data integration.
- Note: Automatic usage retrieval is currently unavailable as Codex CLI does not expose a supported endpoint for this feature. The provider architecture is ready for future implementation.

## Previous Releases

Added tray lifecycle, single-instance activation, startup registration, compact/expanded/auto display modes, global hotkeys, settings, profile management, notifications, docs, CI, packaging scripts, and repository safety checks.
