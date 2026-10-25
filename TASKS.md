# Task: Keep Codex protected across app updates

Codex is currently stored with a versioned `WindowsApps` path. Resolve its installed package dynamically so updates do not require re-adding it. On each launch/restart, choose the highest installed package version with the configured executable; continue matching a running prior-version process by package family until it exits.

Resource status: VERIFIED via Codex usage limits; latest check is 18% used in the 5-hour window and 14% in the 7-day window (82%/86% remaining). Resets: 2026-10-25 16:10:21 UTC and 2026-11-01 06:10:21 UTC.

- [x] Confirm the stable package family name and that the current user can enumerate `WindowsApps` package folders.
- [x] Migrate existing Codex entries and resolve the current executable for attach/restart.
- [x] Add regression coverage for update/path changes and persisted identity (13 checks pass).
- [x] Build version 0.1.41, publish the single-file executable, and verify the live target.
- [ ] Review, commit, and push the changes.

Last verified: 13/13 checks pass; the existing stale setting migrated to package family `OpenAI.Codex_2p2nqsd0c76g0`, saved version 26.1021.3705.0, and attached to running ChatGPT PID 14008.
Next: commit and push the reviewed change.
