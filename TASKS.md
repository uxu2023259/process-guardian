# Task: Keep Codex protected across app updates

Codex is currently stored with a versioned `WindowsApps` path. Resolve its installed package dynamically so updates do not require re-adding it. On each launch/restart, choose the highest installed package version with the configured executable; continue matching a running prior-version process by package family until it exits.

Resource status: VERIFIED via Codex usage limits; latest check is 18% used in the 5-hour window and 14% in the 7-day window (82%/86% remaining). Resets: 2026-10-25 16:10:21 UTC and 2026-11-01 06:10:21 UTC.

- [x] Confirm the stable package family name and that the current user can enumerate `WindowsApps` package folders.
- [x] Migrate existing Codex entries and resolve the current executable for attach/restart.
- [x] Add regression coverage for update/path changes and persisted identity (13 checks pass).
- [x] Build version 0.1.41, publish the single-file executable, and verify the live target.
- [x] Review, commit, and push the changes.

Last verified: 13/13 checks pass; the existing stale setting migrated to package family `OpenAI.Codex_2p2nqsd0c76g0`, saved version 26.1021.3705.0, and is attached to running ChatGPT PID 4672 under ProcessGuardian 0.1.42.
Complete: the reviewed changes are committed and pushed with the process picker release.

---

# Task: Pick and attach a running process by dragging from the app

Outcome: From the add-program panel, drag an in-app target handle over a desktop window. While hovering, identify and highlight that window and show its executable path and PID. Releasing over an attachable process adds it to the protected list and attaches that exact PID without launching a duplicate.

Constraints: Preserve the existing add-by-path flow and settings format; do not persist a transient PID. Releasing must not click or activate the target application. Canceling, an unreadable/exited process, or a persistence failure must leave no new protected entry. An attached process must retain the existing pause/remove/exit safety behavior.

Acceptance criteria:
- [~] Picker and overlay are implemented, but a real cross-window drag/highlight was not observed: CUA's window-bound drag stopped when the preview hid itself; binding the drag to QQ bypassed the preview and was cleared with Esc.
- [x] Regression checks confirm exact-PID attach/persistence, stale and mismatched-target rejection, and rollback on persistence failure.
- [~] The hook consumes the picker press/release in code; actual click-through behavior remains unverified because the desktop drag could not be driven by the available CUA surface. File-picker and existing attach paths remain covered by the existing checks.
- [x] 15/15 checks passed; the add panel was rendered and inspected; Release 0.1.42 is installed at `PLUGIN/ProcessGuardian.exe` and matches the versioned artifact hash. The former 0.1.41 binary is preserved as `PLUGIN/ProcessGuardian-0.1.41.exe`.

Resource preflight (2026-11-08): VERIFIED via Codex Desktop `get_usage_limits`; 5-hour window 13% used and 7-day window 1% used. DeepSeek delegation: FAILED before task execution because the advertised worker returned `deepseek-flash` unsupported with this ChatGPT account; no worker edits were accepted and no retry is planned. DeepSeek quota: UNAVAILABLE — no authoritative provider quota/rate-limit source is exposed in this session.

Progress:
- [x] Inspect existing add flow and exact-process supervision contract.
- [~] DeepSeek overlay presentation delegation was attempted and rejected by the provider; parent owns implementation.
- [x] Implement desktop picker, overlay details/highlight, cancellation, release swallowing, and exact-PID attach with process-start-time validation and rollback on failure.
- [x] Add regression coverage and run it after the implementation; 15/15 checks passed. The isolated UI preview host built with 0 warnings/errors, and the add panel was visually inspected.
- [~] Actual cross-window drag and target overlay could not be exercised through CUA; the app-specific drag stopped when its source window hid, and binding QQ directly bypassed the picker. QQ's temporary message selection was cleared with Esc; no message was sent or deleted.
- [x] Reject `ApplicationFrameHost.exe` as an ambiguous Windows packaged-app host so it cannot be mistaken for the selected application.
- [x] Increment version once to 0.1.42; publish the win-x64 self-contained single-file artifact and verify `PLUGIN/ProcessGuardian-0.1.42.exe` matches the publish output hash.
- [x] PID 13764 was closed manually; 0.1.42 now runs from the standard path as PID 27296 with `--startup`. The Run registry value remains unchanged.
- [x] Review the final diff, confirm `git diff --check`, verify artifact versions/hashes, confirm the official process/startup registration stayed unchanged, and close the isolated preview.

Current milestone: GitHub Release v1.0.1 is published from commit `7a1d46d`. Local ProcessGuardian 0.1.42 remains running from the standard path; `ProcessGuardian.Control list` previously confirmed that Codex PID 4672, PortBridge PID 14028, and Mihomo Party PID 21236 remain attached without restart-count increases. Runtime cross-window drag and click-through remain unverified.
Known constraint: the real desktop picker drag has not been exercised through CUA.
Next: manually exercise the picker with a real desktop pointer; the published release is available at https://github.com/uxu2023259/process-guardian/releases/tag/v1.0.1.

Release delivery (2026-11-08):
- [x] Confirm the current GitHub release is v1.0.0, built from source version 0.1.41, and the repository workflow publishes a win-x64 archive when a `v*` tag is pushed.
- [x] Build a local v1.0.1 release archive from the 0.1.42 source with the repository's .NET SDK 10.0.401; the release workflow will independently build and attach its archive from the pushed tag.
- [x] Commit and push the reviewed feature changes to `main` as `7a1d46d`.
- [x] Push tag `v1.0.1`; GitHub Actions run 42456645358 completed successfully and published `ProcessGuardian-v1.0.1-win-x64.zip` (SHA256 `5be9baabd4411d24527d43a24ba197060ff9fda3c741e1417a90a88921a1452b`).

Release preflight resource status: VERIFIED via Codex Desktop `get_usage_limits` on 2026-11-08; 5-hour window 2% used and 7-day window 4% used. DeepSeek was not used for this release task.
