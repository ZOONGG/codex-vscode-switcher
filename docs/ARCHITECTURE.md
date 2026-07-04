# Architecture

Codex Profile Overlay has two assemblies:

- `CodexProfileOverlay.Core`: token-safe services and models that can be unit-tested without WPF.
- `CodexProfileOverlay`: WPF shell, tray lifecycle, window attachment, hotkeys, settings/profile windows, and Codex process launching.

The switcher keeps the real workspace folders shared, but account-scoped Codex state is stored per profile. During a switch it saves `%USERPROFILE%\.codex\auth.json` plus the known Codex chat/session index files into `%USERPROFILE%\.codex-profiles\<profile>`, then restores the selected profile's saved state. Non-secret UI metadata lives under `%LOCALAPPDATA%\CodexProfileOverlay`.

Normal Codex launches do not set `CODEX_HOME`. The add-profile login flow sets `CODEX_HOME` only for that isolated `codex login` process.

## Profile Status and Usage Limits

The status feature allows users to track which Codex profiles have usable limits:

- `ProfileStatusMetadata`: Stored per-profile user metadata (manual emoji, labels, notes, reset times). Saved to `profile-status.json`, never mixed with credentials.
- `UsageSnapshot`: Automatic usage data from a provider. Contains remaining percentages for each limit window, exhaustion state, and timestamps.
- `IUsageProvider`: Abstraction for retrieving usage data. Returns `Supported` or `Unavailable` capability. Currently only `UnavailableUsageProvider` is implemented since Codex CLI does not expose a supported endpoint for usage data.
- `ProfileStatusService`: Manages status document loading/saving, calculates limit indicator emoji (🟢, 🟡, 🔴), and finds the recommended profile based on lowest remaining percentage.

Limit indicators appear as small emoji next to profile names in the overlay:
- ⭐: Recommended profile (only when ≥2 profiles have fresh comparable data)
- 🟢: High remaining capacity (≥ green threshold, default 60%)
- 🟡: Medium remaining capacity (≥ yellow threshold, default 25%)
- 🔴: Low or exhausted (below yellow threshold)
- No emoji: Unknown, unavailable, or stale data

The recommendation algorithm ranks profiles by:
1. Exhausted profiles are ranked last
2. Profiles with stale or failed data rank below fresh data
3. Profiles with unknown data rank below known data
4. Lowest remaining percentage across all available limit windows
5. Average remaining percentage as tie-breaker
6. Nearest reset time as additional tie-breaker
