# WinQuickSwitch v1.1.0.0 - test candidate

Implementation and automated release gates are complete. Built September 7,
2026. This is a local candidate for attended testing, not a Store submission or
hardware sign-off. The next feature milestones remain v1.1.1.0 and v1.1.2.0.

## Start here

Exit the older resident WinQuickSwitch through its tray menu first. Then open
[Paid Portable v1.1.0.0](../artifacts/win-x64-paid-portable-1.1.0.0/WinQuickSwitch.exe)
and confirm **1.1.0.0** in About. Portable includes the runtime; Lite requires
.NET 10 Desktop Runtime. Paid standalone builds are private test artifacts,
not a replacement for Store licensing or public distribution.

## Downloads

| Edition | Standalone | Microsoft Store |
| --- | --- | --- |
| Paid | [Paid Lite v1.1.0.0](../artifacts/win-x64-paid-lite-1.1.0.0/WinQuickSwitch.exe), [Paid Portable v1.1.0.0](../artifacts/win-x64-paid-portable-1.1.0.0/WinQuickSwitch.exe) | [Paid Store upload v1.1.0.0](../artifacts/store-msix/paid/v1.1.0.0/WinQuickSwitch_1.1.0.0_x64.msixupload), [Paid Store MSIX v1.1.0.0](../artifacts/store-msix/paid/v1.1.0.0/WinQuickSwitch_1.1.0.0_x64.msix) |
| Free | [Free Lite v1.1.0.0](../artifacts/win-x64-free-lite-1.1.0.0/WinQuickSwitch.exe), [Free Portable v1.1.0.0](../artifacts/win-x64-free-portable-1.1.0.0/WinQuickSwitch.exe) | [Free Store upload v1.1.0.0](../artifacts/store-msix/free/v1.1.0.0/WinQuickSwitch-Free_1.1.0.0_x64.msixupload), [Free Store MSIX v1.1.0.0](../artifacts/store-msix/free/v1.1.0.0/WinQuickSwitch-Free_1.1.0.0_x64.msix) |

Store packages are unsigned local outputs; Microsoft signs accepted submissions.
They are not trusted end-user installers. Both identities and the exact Free
reserved name, **WinQuickSwitch - Free**, were validated.

[SHA-256 hashes and byte sizes for all eight outputs](../artifacts/releases/v1.1.0.0/SHA256SUMS.json).

## Candidate changes

- Stabilized shared views, content fitting, persisted geometry, theme rendering,
  compact navigation, accessibility and resident-window recovery.
- First-run cards use actual fitted/minimum sizes. Restored/custom positions
  and intentional overlap are preserved. Hidden views are constructed lazily.
- Complete-content measurement includes scroll-container chrome; bottom actions
  remain visible after fitting.
- Display, Audio and Devices expose header Refresh controls that do not fit or
  reconstruct cards. Profile-target labels and contradictory status were fixed.
- Display-transition verification remains bounded and topology-safe; real
  fullscreen-game reproduction is not claimed.

## Automated results

| Gate | Result |
| --- | --- |
| Application regression | 150/150 passed |
| Read-only integration run | 157/157 passed, including the 150 regression checks |
| Performance-analysis fixtures | 20/20 passed |
| Paid screenshot/UI checks | 71/71 passed, 29 screenshots |
| Free screenshot/UI checks | 62/62 passed, 26 screenshots |
| Performance | Both editions passed; 20 main and 20 overlay measured cycles per edition plus warm-ups; zero enforced-budget failures |
| Release artifacts | 39/39 checks passed |
| Release builds | Paid/Free passed with zero warnings/errors |

- [Paid screenshots v1.1.0.0](../artifacts/sandbox-ui/paid/20260906-164203-v1.1.0.0/results/index.html)
- [Free screenshots v1.1.0.0](../artifacts/sandbox-ui/free/20260906-215030-v1.1.0.0/results/index.html)
- [Speed and memory comparison v1.1.0.0](../artifacts/sandbox-performance/comparison/20260906-223927-v1.1.0.0/performance.html)
- [Resource margins and historical results](PERFORMANCE_CLOSEOUT_2026-09-04.md)

## Remaining acceptance

Attended acceptance remains before release sign-off: mixed DPI/monitors,
device hot-plugging, native tray recovery, real Windows accessibility, the
reported fullscreen-game display issue, and Store install/update behavior.
Use the [v1.1.0.0 checklist](RELEASE_ACCEPTANCE_1_1_0.md). Exclusive-fullscreen
overlay support is not promised. No Store submission or Git push was performed.
