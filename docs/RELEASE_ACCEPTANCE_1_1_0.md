# v1.1.0.0 release acceptance

Status: **not signed off**. The user confirmed on September 5, 2026 that
real-hardware acceptance has not been performed. No Store submission or
publication is implied by local build or test results.

## Automated prerequisites

- [x] Paid and Free Release builds: zero warnings and errors.
- [x] Application regression (150/150) and read-only integration (157/157) suites pass.
- [x] Sandbox harness and performance-analysis fixtures (20/20) pass.
- [x] Paid and Free screenshot/UI suites pass on the final header-refresh build:
      71/71 and 62/62, with 55 screenshots.
- [x] Full Paid and Free 20-iteration speed/resource comparison passes the
      original retained-resource limits; incomplete runs do not count.
- [x] Candidate executable versions and both Store identities are verified.
- [x] Store upload containers match their standalone MSIX packages and exclude
      the simulated E2E service implementation.

See [performance closeout](PERFORMANCE_CLOSEOUT_2026-09-04.md) for historical
results and corrections. Check each item only against its actual run evidence.
A downloadable test candidate is not a completed release; physical acceptance
below still gates submission.

## Attended checks on the candidate

Record the executable/package version and edition, Windows build, monitor
scaling, steps, observed result, and screenshots for each check. Use generic
device/profile names in screenshots intended for the Store.

Before testing a standalone candidate, exit the existing WinQuickSwitch from
its tray menu. An already-running older edition can receive the new process's
activation request. Confirm the opened app reports **v1.1.0.0** in About before
recording results. Portable includes its runtime; Lite requires .NET 10 Desktop
Runtime. These builds should be tested before any Store submission.

| Area | Check | Status |
| --- | --- | --- |
| Startup and tray | With overlay enabled, launch once, dismiss, and use tray Open. Repeat with the conventional window selected. Verify tray Exit closes every app surface. | Not tested |
| Geometry | Move and resize Audio/Devices, overlap cards, dismiss/reopen, and verify exact saved bounds. Hide/restore a card and change theme while open. | Not tested |
| DPI and monitors | Check 100%, 150%, and 200% scaling, mixed-DPI and negative-coordinate monitors. Open on each pointer monitor; verify launcher, resize bounds, and readable content. | Not tested |
| Monitor removal | Disconnect a secondary monitor only when safe to interrupt that display. Verify reachable/clamped widgets and preserved placement/rotation when restored. | Not tested |
| Hot-plug | Connect/disconnect a USB audio endpoint and a Bluetooth accessory while Audio/Devices are open. Verify refresh, selection, feedback, and continued dragging/resizing. | Not tested |
| Accessibility | Navigate by keyboard, inspect screen-reader names and results, and enable actual Windows High Contrast. Check dropdowns, checked labels, sliders, and focus. | Not tested |
| Fullscreen game | Reproduce the reported Valorant scenario during a safe attended session; record topology before/after and any override warning. If reproduction fails, record that rather than claiming a fix was physically verified. | Not tested |
| Editions | Free has no Profiles route or shortcut; Paid capture/apply/Undo and profile shortcuts work. Use a harmless profile or carefully review included fields first. | Not tested |
| Store packaging | Install/update each edition through the appropriate trusted Store test path; confirm edition identity, settings retention, startup, and side-by-side behavior. Unsigned local MSIX files are not normal end-user installers. | Not tested |

Before display-changing checks, record the working Windows display arrangement
and rotation and avoid running them during important work. Automated tests use
simulated devices and never establish that a real GPU, driver, game, radio, or
Windows notification-area interaction has passed.

## Sign-off record

- Candidate: [v1.1.0.0 downloads](RELEASE_1_1_0.md), built September 7, 2026.
  [Verified artifact hashes](../artifacts/releases/v1.1.0.0/SHA256SUMS.json).
  Artifact validation passed 39/39 checks.
- UI evidence: [Paid v1.1.0.0](../artifacts/sandbox-ui/paid/20260906-164203-v1.1.0.0/results/index.html),
  [Free v1.1.0.0](../artifacts/sandbox-ui/free/20260906-215030-v1.1.0.0/results/index.html).
- Performance evidence: [v1.1.0.0 full comparison](../artifacts/sandbox-performance/comparison/20260906-223927-v1.1.0.0/performance.html),
  both editions passed with unchanged enforced budgets and complete coverage.
- Attended tester/date/results: pending.
- Supported limitations: real mixed-DPI/hot-plug/tray/game behavior remains
  unverified; exclusive-fullscreen overlay support is not promised. Resource
  margins and historical failures are documented in the performance closeout.
- Store submission/publication: not performed.
