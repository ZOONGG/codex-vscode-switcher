# Architecture

Codex Profile Overlay has two assemblies:

- `CodexProfileOverlay.Core`: token-safe services and models that can be unit-tested without WPF.
- `CodexProfileOverlay`: WPF shell, tray lifecycle, window attachment, hotkeys, settings/profile windows, and Codex process launching.

The switcher keeps the real workspace folders shared, but account-scoped Codex state is stored per profile. During a switch it saves `%USERPROFILE%\.codex\auth.json` plus the known Codex chat/session index files into `%USERPROFILE%\.codex-profiles\<profile>`, then restores the selected profile's saved state. Non-secret UI metadata lives under `%LOCALAPPDATA%\CodexProfileOverlay`.

Normal Codex launches do not set `CODEX_HOME`. The add-profile login flow sets `CODEX_HOME` only for that isolated `codex login` process.

## Profile Status and Usage Limits

The status feature allows users to track which Codex profiles have usable limits:

- `ProfileStatusMetadata`: Stored per-profile user metadata (manual emoji, labels, notes, reset times). Saved to `profile-status.json`, never mixed with credentials.
- `UsageSnapshot`: Automatic usage data from a provider. `UsageLimitWindow` preserves provider window names, optional durations, remaining percentages, and reset timestamps instead of assuming fixed 5-hour/7-day windows.
- `IUsageProvider`: Abstraction for retrieving usage data. Returns `Supported` or `Unavailable` capability. Currently only `UnavailableUsageProvider` is implemented since Codex CLI does not expose a supported endpoint for usage data.
- `ProfileStatusService`: Validates manual metadata, serializes provider calls with cancellation/timeouts, preserves successful cache entries on failure, and calculates recommendations.
- `UsageIntelligence` and `ProfileIndicatorFormatter`: Pure, tested capacity and emoji formatting rules used by the WPF shell.

The status document is schema-versioned and normalized on load. Timestamps are persisted as UTC and formatted in local time. Legacy short/long snapshot fields remain readable for backward compatibility. Manual metadata and automatic snapshots are independent, so refreshes cannot overwrite user notes.

### Provider investigation

The installed `codex-cli 0.142.5` was checked through its public `--help` command tree. It contains no supported usage or limits command. Official OpenAI Codex documentation was searched for a machine-readable CLI command, documented local usage API/IPC, and documented non-secret metadata file; none was found. API model rate-limit tables are not per-ChatGPT-account remaining-capacity data and are not used.

Consequently, the runtime wires `UnavailableUsageProvider`. It does not read credential files, start isolated profile processes, call undocumented endpoints, or schedule refresh work. The automatic master setting is forced off while the capability is unavailable. A future provider must use a documented supported contract and return normalized snapshots without exposing raw payloads.

Limit indicators appear as small emoji next to profile names in the overlay:
- ⭐: Recommended profile (only when ≥2 profiles have fresh comparable data)
- 🟢: High remaining capacity (≥ green threshold, default 60%)
- 🟡: Medium remaining capacity (≥ yellow threshold, default 25%)
- 🔴: Low or exhausted (below yellow threshold)
- No emoji: Unknown, unavailable, or stale data

The recommendation algorithm only considers fresh, known, non-exhausted snapshots. It maximizes the lowest remaining percentage across all available windows, then the average remaining percentage, then the nearest reset. It returns no recommendation with fewer than two candidates or when the leading candidates are indistinguishable.

### Overlay performance

The controller reuses a validated Codex window handle and performs a full top-level window scan only when attachment is lost. Placement is recalculated on a background-priority UI tick, but WPF layout and `SetWindowPos` run only after geometry actually changes. Launch-on-start first checks for an existing Codex window and never starts a duplicate instance. Tick failures are contained and logged rather than terminating the tray process.
