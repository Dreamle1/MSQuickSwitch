# Implementation and test log

This file records completed slices, important decisions, and verification
evidence. Attended hardware checks are kept separate from automated tests so a
normal test run never changes display or audio state.

## 2026-09-09 - overlay click-away dismissal

### Implemented

- Removed the redundant global close button above the overlay launcher.
- Made clicks on bare overlay, workspace, and widget-canvas surfaces close the
  workspace while preserving normal interaction with launcher entries,
  inspectors, widget cards, and their controls.
- Kept Escape dismissal, inspector close, per-widget removal, and the explicit
  Exit WinQuickSwitch command unchanged.
- Added an isolated WPF routing regression and a native Sandbox pointer-click
  check so the behavior is validated through both the visual tree and the
  rendered application.

### Verification

- Paid and Free regression suites pass 168/168 tests.
- Paid and Free Release builds complete with zero warnings and zero errors.
- The hidden Paid Sandbox UI run passes 84/84 checks with 31 screenshots,
  including absence of the redundant close control and successful dismissal by
  clicking empty overlay background. Background cleanup left no visible
  Sandbox windows or test processes.
- Evidence: [Paid v1.1.1.0 overlay gallery](../artifacts/sandbox-ui/paid/20260909-173337-v1.1.1.0/results/index.html).

## 2026-08-30 - v1.1.1 shortcut registration reliability

### Implemented

- Replaced action-only native cleanup with a complete registration ledger, so
  reapply and shutdown unregister dynamic Paid profile hotkeys as well as
  built-in shortcuts.
- Made live shortcut replacement transactional after a working set exists. If
  a new binding conflicts or registration fails, the partial replacement is
  removed and the previous native set and action routing are restored.
- Normalized duplicate shortcuts inside the Paid profile catalog by retaining
  the first owner and clearing later duplicates.
- Added pre-registration ownership checks for profile shortcuts that collide
  with built-in or other profile commands, including a clear message naming
  the existing owner and affected profile.
- Replaced positional profile hotkey IDs with deterministic IDs derived from
  stable profile IDs, and reserved `Ctrl+0` through `Ctrl+6` for overlay
  navigation.
- Added an edition-aware `QuickSwitchCommandCatalog` with stable keys, labels,
  categories, availability rules, and typed command factories for every
  persisted built-in hotkey plus dynamic Paid profile Apply commands.
- Replaced the window's parallel global-hotkey action switch with catalog
  resolution and coordinator execution. Display modes, favorites, panel opens,
  configured-surface toggling, overlay toggling, and Paid profile shortcuts now
  share the same typed result and smart-close pipeline as feature controls.
- Moved shared Options shortcut labels and Paid profile Apply controls onto the
  catalog so those surfaces no longer define their own command identities.
- Routed native tray Open, Exit, and favorite selections through catalog-created
  typed commands while preserving the required deferred execution after the
  Windows popup menu closes.
- Replaced the overlay workspace's panel/Open/Exit callback split with one typed
  command callback. Launcher selection is edition-validated before showing an
  inspector, and a navigation revision prevents a superseded refresh from
  reopening stale content after a newer selection or overlay close.
- Kept collapse, content-fit, remove, drag, and resize behavior inside the
  workspace shell rather than turning visual geometry into coordinator
  mutations. Removed the duplicate card-header Refresh control because the
  shared complete feature views already own their refresh actions.
- Removed the unused string-action builder and callbacks from floating Audio
  and Devices windows. Those hosts now expose only their shared typed feature
  view instances plus local window layout controls.

### Verification

- Added regression coverage for profile unregistration during reapply and
  disposal, failed-replacement rollback, duplicate profile normalization, and
  conflict ownership feedback.
- Added catalog coverage for unique persisted action mappings, exact typed
  payloads, Free/Paid profile availability, and source-level routing ownership.
- The complete isolated suite passes 143/143 tests, including Paid/Free
  launcher command composition, typed overlay footer routing, stale navigation
  rejection, and the presentation-only geometry boundary.
- Paid and Free Release builds complete with zero warnings and zero errors.
- The public development version remains 1.0.33.0 while the rest of v1.1.1 is
  implemented and attended release gates remain pending.

## 2026-08-29 - v1.1.0 coordinator-state boundary cleanup

### Implemented

- Made the shared coordinator's cached immutable snapshot authoritative for
  display topology, Audio and Devices inventories, load/busy state, and
  feature status messages. Reading `IQuickSwitchCoordinator.State` no longer
  queries Windows or inspects hidden legacy WPF controls.
- Copied platform inventory collections before publishing them so a previously
  published snapshot cannot change underneath an open view.
- Routed display, audio favorite, option, device, and profile command results
  into coordinator-owned feature status before publishing the next revision.
- Removed the unused compact Display/Audio/Devices workspace builders and the
  fallback inspector/action-list builders. Workspace cards and inspectors now
  have one active construction path through `FeatureViewFactory`.

### Verification

- Added source-boundary regression gates proving shared snapshots do not read
  legacy WPF lists, buttons, or status text and that removed duplicate builders
  cannot silently return.
- Paid and Free Release builds completed with zero warnings and zero errors.
- The isolated regression suite passed 132/132 tests.
- The static Windows Sandbox harness passed. Hidden end-to-end runs passed
  43/43 Paid checks with 26 screenshots and 37/37 Free checks with 23
  screenshots. Both editions completed without a Sandbox failure dialog.
- The public development version remains 1.0.33.0 until all v1.1.0 acceptance
  checks are complete.

## 2026-08-25 - Audio popup theme and session table repair (v1.0.29.0)

### Fixed

- Added a local palette scope to the main window, workspace, sidebar, shared
  feature views, floating windows, and legal window. Detached WPF popups now
  keep the active dark or light theme when the setting changes while they are
  open.
- Bound the ComboBox drop-down surface to its placement target's palette and
  explicitly applied the shared ComboBox and item styles, preventing the
  Windows default white role menu from reappearing.
- Replaced the main Audio panel's fragile GridView app-volume list with the
  same explicit, scrollable application/output/volume/mute table used by the
  shared Audio view, including a clear empty state.

### Verification

- Release build completes with zero warnings or errors.
- The complete automated suite passes 124/124.

## 2026-08-25 - Audio theme and app-volume presentation (v1.0.28.0)

### Fixed

- Replaced the default WPF ComboBox popup with a palette-aware template so
  open role menus remain readable in dark and light themes, including when the
  theme changes while a widget is open.
- Kept the latest audio inventory in coordinator state so shared views do not
  depend on hidden legacy list controls for endpoint or app-session data.
- Rebuilt the App volumes area with stable headers, endpoint names, volume
  sliders and labels, mute controls, scrolling, and an explicit empty state.

### Verification

- Added coverage for coordinator-backed app-volume sessions and shared-view
  template rendering.
- The complete automated suite passes 124/124 with zero build warnings or
  errors.

## 2026-08-25 - Options rendering hotfix (v1.0.27.0)

### Fixed

- Corrected the shared Options shortcut display field to use an explicit
  one-way WPF binding. `TextBox.Text` otherwise defaulted to two-way binding
  and threw a `XamlParseException` when the normal Options panel first
  rendered because `ShortcutText` is intentionally read-only.
- Added an STA WPF regression test that creates, attaches, measures, arranges,
  and renders the complete shared Options view so template-only binding errors
  are exercised rather than merely checking factory metadata.

### Verification

- Windows Application events identified the exact v1.0.26.0 exception and
  binding property.
- The complete automated suite passes 123/123 with zero build warnings or
  errors.

## 2026-08-24 - shared full-UI widgets (v1.0.26.0)

### Implemented

- Added reusable Display, Audio, Devices, Options, Profiles, and About feature
  views plus an edition-aware factory. The main window, overlay launcher,
  workspace cards, and supported floating hosts create distinct controls from
  that factory while sharing one coordinator and immutable state snapshots.
- Replaced compact workspace summaries and action lists with complete shared
  controls. Removed the card Expand footer; the right launcher remains the
  parallel full-feature navigation path.
- Advanced workspace persistence to schema version 6 with content-fit version
  tracking, exact user geometry, first-ready measurement, monitor-safe caps,
  overlap preservation, 36-DIP cascading placement, and no refresh-time card
  reconstruction.
- Added deferred tray Open and post-load reactivation so a resident hidden
  instance can reliably return after the native notification-area menu closes.
- Kept all display topology, placement restoration, and rotation safety paths
  unchanged.

### Verification

- Factory parity covers all enabled surfaces and Free rejects every Profiles
  view route.
- Geometry tests cover first fit, reset, reopen, refresh stability, overlap,
  z-order, monitor clamping, migration, and feature resize policy.

## 2026-08-23 - stable widget geometry and content-based resizing (v1.0.21.0)

### Implemented

- Persisted widget width and height are authoritative across reopen and content
  refresh, including legacy layouts with `HasCustomSize` set to false.
- Overlay collision repair, card swapping, free-slot relocation, and resize
  rollback were removed; cards may overlap while z-order remains selectable.
- Added feature-level resize policy. Audio, Devices, Favorites, and Paid
  Profiles remain resizable; static Display, Options, About, and Home cards do
  not expose resize controls. Display uses its fixed 500x260 DIP preset.
- Advanced layout settings to version 3 and migrated static cards to their
  fixed presets once while preserving positions, visibility, and z-order.
- Deferred widget refreshes remain safe during drag and resize gestures.

### Verification

- Added regression coverage for saved dimensions, intentional overlap,
  static-widget migration, and resize-policy metadata.
- Release artifacts are labeled v1.0.21.0 after the complete automated suite
  passes.

## 2026-08-23 - overlay widget input and persistence repair (v1.0.20.0)

### Implemented

- Delayed the first workspace arrangement until the monitor-sized canvas has
  completed its WPF measurement. Restored coordinates are no longer clamped
  against the transient pre-show canvas.
- Made the complete widget header draggable, while keeping buttons and resize
  thumbs out of the drag route.
- Preserved the active drag/resize visual tree during background refreshes.
- Added collision-safe drag drops: a single occupied target swaps cards, and
  impossible drops return to the pre-gesture bounds instead of overlapping.
- Restored the previous size when an invalid resize would overlap another card.
- Captured the live layout before overlay close and again during close-event
  cleanup so the final position and size survive reopening.

### Verification

- Windows control reproduction confirmed the resize thumb was previously
  swallowed by the card mouse handler and that reopen clamped a saved custom
  position to the pre-show canvas. The repaired debug run confirmed custom
  size, free-space moves, occupied-slot swaps, and close/reopen persistence.
- Automated checks: **116/116 passed**.
- Temporary UI-inspection window, title, and settings-path hooks were removed
  before the release build.

## 2026-07-24 - display switching slice

### Implemented

- Added the `DisplayMode` model, `DisplayModeResult`, and
  `IDisplayModeService` application boundary.
- Added a Windows adapter that maps the four allowlisted modes to
  `DisplaySwitch.exe` arguments and starts the process without a shell window.
- Added working display buttons and result status in the WPF window.
- Added a warning confirmation for PC-screen-only and second-screen-only modes,
  because either can turn off the display currently being used.
- Added cancellation when the application window closes.
- Added a dependency-free automated test executable. It uses a fake process
  runner and never invokes `DisplaySwitch.exe`.

### Design decisions

- Raw command strings never come from UI input.
- Process exit code zero is success; non-zero codes become visible failure
  results.
- Known process-start failures become user-facing results. Cancellation remains
  cancellation and is not converted into an error.
- The process runner is an internal seam exposed only to the test assembly.
- A lightweight test executable is used for this first slice so the repository
  needs no test-framework NuGet packages. A standard test framework can be
  introduced when coverage and CI reporting justify the dependency.

### Automated verification

Run:

```powershell
.\.dotnet\dotnet.exe format .\WinQuickSwitch.sln --verify-no-changes --no-restore
.\.dotnet\dotnet.exe build .\WinQuickSwitch.sln
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj
```

Environment:

- Project-local .NET SDK: `10.0.302`
- .NET Windows Desktop Runtime: `10.0.10`

Recorded results:

- Format verification: passed with no changes required.
- Release solution build: passed.
- Compiler warnings: 0.
- Compiler errors: 0.
- Automated checks: 10 passed, 0 failed.
- Hardware-changing commands invoked by tests: 0.

The tenth check exercises the real hidden-process runner against `cmd.exe` and
verifies its exit code. The other checks use a fake runner to keep the
`DisplaySwitch.exe` boundary isolated.

The first restore initially failed in the restricted sandbox while accessing
NuGet. It succeeded after network access was approved. The projects have no
third-party package references; restore resolved only SDK/framework assets.

### Attended hardware verification

Not run automatically. On a machine with two displays, manually verify:

1. Duplicate shows the same desktop on both displays.
2. Extend creates a continuous desktop.
3. PC screen only shows a warning, then switches only after confirmation.
4. Second screen only shows a warning, then switches only after confirmation.
5. Choosing **No** in either warning leaves the display topology unchanged.
6. Return the machine to its original topology after testing.

### Remaining M1 work

- Run the attended one- and two-monitor matrix.

## 2026-07-24 - display topology and read-only audio inventory

### Implemented

- Added a `QueryDisplayConfig` adapter for active and available display paths.
- Added classification for PC-screen-only, second-screen-only, duplicate, and
  extend topologies.
- Reports hybrid duplicate-plus-extend layouts as custom/mixed instead of
  inventing a global mode.
- Disables duplicate and extend when Windows reliably reports fewer than two
  available displays.
- Added Core Audio endpoint enumeration for active playback and recording
  devices.
- Tracks console, multimedia, and communications default roles separately.
- Added active application-session inventory with output endpoint, current
  volume, and mute state.
- Added a manual audio refresh and populated endpoint/session views.
- Kept endpoint identifiers and session identifiers in memory; they are not
  written to diagnostic output.

### Automated verification

Commands:

```powershell
.\.dotnet\dotnet.exe format .\WinQuickSwitch.sln --verify-no-changes --no-restore
.\.dotnet\dotnet.exe build .\WinQuickSwitch.sln --configuration Release --no-restore
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build -- --integration
```

Recorded results:

- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 19 passed, 0 failed.
- Read-only live checks: 21 passed, 0 failed including the isolated checks.
- Live display snapshot: extend, 3 active, 3 available.
- Live audio snapshot: 7 playback endpoints, 3 recording endpoints, 1 active
  session.
- Display-changing commands invoked by verification: 0.
- Volume/mute changes invoked by verification: 0.
- Self-contained single-file publish: passed.
- Published artifact: `artifacts/win-x64-m2/WinQuickSwitch.exe`
  (`71,438,528` bytes).
- Published SHA-256:
  `09D3D4049D48C483098390D50AE3E09B4940A50135D534579DD03D59C9FCF26A`.

The existing `artifacts/win-x64/WinQuickSwitch.exe` was running and therefore
locked by Windows during the first publish attempt. It was left running and the
new milestone build was written to `artifacts/win-x64-m2` instead.

### Remaining work

- M1 still needs attended switching tests on one- and two-monitor systems.
- M2 still needs endpoint and session change notifications. Until then, use the
  refresh button after connecting a device or starting/stopping audio.

## 2026-07-24 - optimized .NET 10 deployment

### Implemented

- Changed the app and test targets from the versioned Windows SDK TFM to
  `net10.0-windows`.
- Removed the unused runtime dependency on `Microsoft.Windows.SDK.NET.dll`
  (`24.9 MB`) and `WinRT.Runtime.dll` (`0.5 MB`).
- Added `global.json` to stay on supported .NET 10 SDK feature bands.
- Added checked-in `Lite` and `Portable` publish profiles.
- Stopped enumerating Core Audio during startup. Audio state loads only after
  the user selects **Refresh**.
- Kept the app single-process with no service or resident tray process.

### Verification

- Release solution build: passed with 0 warnings and 0 errors.
- Isolated and live read-only checks: 21 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 playback endpoints, 3 recording endpoints, 1 active session.
- Display or audio mutations invoked by verification: 0.

### Published variants

| Variant | Runtime model | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Lite | Framework-dependent | 219,339 | `ECCD301B0757FBE90A3FAD14FBD924C51C50D6FC97E9F8452FF7106BA86C6743` |
| Portable | Self-contained | 64,819,933 | `4A88B189D64289C15BA2590600F191F020EB910C944E8573B68B09DED8775A76` |

Outputs:

- `artifacts/win-x64-lite/WinQuickSwitch.exe`
- `artifacts/win-x64-portable/WinQuickSwitch.exe`

## 2026-07-24 - dark control theme

### Implemented

- Added dark templates for buttons, list boxes, list views, and GridView
  headers so they no longer fall back to white system control surfaces.
- Added readable hover, pressed, disabled, selected, and keyboard-focus states.
- Added dark scrollbars for the page and audio inventory lists.
- Kept the palette resource-driven so future light-theme support can swap the
  brushes without replacing control templates.

### Verification

- Release solution build: passed with 0 warnings and 0 errors.
- Automated checks: 19/19 passed.

## 2026-07-24 - live Core Audio notifications (M2 complete)

### Implemented

- Added a dedicated background MTA watcher for endpoint and audio-session
  notifications; no service, tray process, or polling loop was introduced.
- Registered endpoint add, remove, state, property, and default-role callbacks.
- Registered session-created, volume, state, and disconnect callbacks for every
  active playback endpoint.
- Added a 350 ms debounce so bursts of Windows callbacks result in one inventory
  refresh rather than repeated COM enumeration and UI work.
- Rebuilds session subscriptions only after callbacks return, keeping Core
  Audio callbacks nonblocking and avoiding registration deadlocks.
- Unregisters callbacks and releases retained COM objects on window close.
- Preserves the Refresh button and shows a fallback status if live registration
  cannot start.

### Automated verification

Commands:

```powershell
.\.dotnet\dotnet.exe format .\WinQuickSwitch.sln --verify-no-changes --no-restore
.\.dotnet\dotnet.exe build .\WinQuickSwitch.sln --configuration Release --no-restore
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build -- --integration
```

Recorded results:

- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 21 passed, 0 failed.
- Read-only live checks: 24 passed, 0 failed.
- Live watcher registration and deterministic unregistration: passed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 playback endpoints, 3 recording endpoints, 1 active session.
- Display or audio mutations invoked by verification: 0.

Published variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 234,187 | `4CC46D4B9BEE82E8DD2D5E1CF2EC2887059C70E77F6B2DB992C01AE8244B30D3` |
| Portable | 64,826,007 | `7055754E824C2703E700C9CD87D83FA9CFE724E9D89287CBE2CE22426B20608A` |

### Attended notification verification

1. Start WinQuickSwitch and confirm the audio inventory appears without
   selecting Refresh.
2. Start an application that creates an audio session; confirm it appears in
   the session table within approximately one second.
3. Close that application; confirm its session disappears after Windows marks
   it inactive or expired.
4. Connect and disconnect a USB or Bluetooth audio device; confirm the playback
   and microphone lists update without selecting Refresh.
5. Change the Windows default playback or communications endpoint; confirm the
   inventory refreshes and remains responsive.
6. Close WinQuickSwitch and confirm the process exits without a resident
   watcher thread.

No attended step should change display topology or audio volume from inside
WinQuickSwitch. Use Windows or the test application to create the external
events.

### Attended verification result

On 2026-07-25, the user reported that the M2 attended notification checks were
verified. This is user-attested confirmation; the automated log above separately
records only the read-only checks executed by the test program.

## 2026-07-24 - automatic audio startup refresh

### Implemented

- Audio inventory now loads automatically when the main window finishes
  starting; the Refresh button remains available for subsequent updates.
- Corrected the dark list-item templates so endpoint names and audio-session
  columns render their bound values instead of record debug text.

### Verification

- Release solution build: passed with 0 warnings and 0 errors.
- Automated checks: 19/19 passed.

## 2026-07-25 - application audio controls and endpoint roles (M3)

### Implemented

- Added per-session volume sliders that commit only after mouse release or a
  keyboard adjustment; loading and refreshing the UI never writes audio state.
- Added per-session mute toggles with failure rollback and automatic inventory
  refresh after each requested change.
- Resolves every mutation by session-instance identifier across active playback
  endpoints, avoiding process-name collisions.
- Added explicit **Set default** and **Set calls** actions for both playback and
  microphone lists. General selection updates console and multimedia roles;
  calls selection updates communications only.
- Isolated the compatibility-sensitive policy COM declaration behind
  `IDefaultAudioEndpointSetter`.
- Opens the documented Windows Sound Settings page if direct endpoint selection
  is unsupported, while keeping the error visible in the app.
- Added dark, keyboard-focusable Slider and CheckBox templates and screen-reader
  names for each application control.

### Automated verification

Commands:

```powershell
.\.dotnet\dotnet.exe format .\WinQuickSwitch.sln --verify-no-changes --no-restore
.\.dotnet\dotnet.exe build .\WinQuickSwitch.sln --configuration Release --no-restore
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build -- --integration
```

Recorded results:

- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 27 passed, 0 failed.
- Read-only live checks: 30 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 playback endpoints, 3 recording endpoints, 1 active session.
- Non-mutating policy compatibility probe: COM class activation and interface
  query passed (`HRESULT 0x00000000`); endpoint mutations invoked: 0.
- Actual WPF Release window startup, layout, and normal shutdown: passed.
- Audio mutations invoked by automated verification: 0.

Published variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 250,571 | `C0CBF721FC853103501AFF62054E554C6CE3F19228E1C07EA617C84C744AE63F` |
| Portable | 64,832,388 | `3F1968ACA4DF25CA024711A294A23247E3FECD7E9B435138C107D4AEB5851C30` |

### Attended M3 verification

1. Start an ordinary desktop audio application and note its current Windows
   mixer volume and mute state.
2. Move only that application's WinQuickSwitch slider. Confirm the Windows
   mixer reports the same percentage and other applications do not change.
3. Toggle Mute on and off. Confirm sound and the Windows mixer follow the toggle,
   then restore the application's original volume and mute state.
4. Select a playback endpoint and choose **Set default**. Confirm Windows moves
   ordinary audio to it, then restore the original default playback endpoint.
5. Select a playback endpoint and choose **Set calls**. Confirm the
   communications role changes independently, then restore it.
6. Repeat steps 4 and 5 for a microphone, restoring both original recording
   roles afterward.
7. If direct selection reports unsupported, confirm Sound Settings opens and
   the same change can be completed there.
8. Close WinQuickSwitch and confirm the process exits without retaining an audio
   session or watcher thread.

The endpoint role checks intentionally remain attended because they change the
user's active Windows audio configuration. Automated tests use fake policy and
Settings adapters and never mutate the machine.
- Actual WPF Release window launched with the local .NET 10 runtime and showed
  7 playback devices, 3 microphones, and 2 active application sessions without
  pressing Refresh.
- Release executable launched successfully after the theme change.

## 2026-07-24 - audio labels and adaptive window sizing

### Implemented

- Audio endpoint lists now show only the Windows friendly device name. Default
  role metadata remains available internally for ordering and state logic.
- The main window now sizes itself to the available content vertically, capped
  by the usable desktop work area, so the display, audio, devices, and status
  sections are visible together on normal screens.
- Endpoint list heights were increased to show the connected options without
  immediately requiring nested scrolling.

### Verification

- Release solution build: passed with 0 warnings and 0 errors.
- Automated checks: 19/19 passed.

## 2026-07-25 - connected Bluetooth and wired devices (M4)

### Implemented

- Added a dependency-free SetupAPI adapter that enumerates only present
  Plug-and-Play devnodes and reads friendly name, class, enumerator, container
  ID, hardware ID, started state, and problem code.
- Classifies Bluetooth and USB-wired devices, groups interfaces sharing a
  physical container, and hides Windows hub/enumerator/protocol infrastructure.
- Removes known Bluetooth profile labels and collapses exact normalized names,
  so media, hands-free, and LE profiles do not become misleading duplicate
  rows.
- Added a dark device table with friendly name, connection, type, and status.
- Added manual Refresh plus automatic startup refresh.
- Hooks the WPF window's `WM_DEVICECHANGE` messages and debounces bursts by
  450 ms. There is no polling loop, background service, or tray process.
- Added allowlisted Bluetooth and Connected Devices Settings shortcuts.
  Pairing, removal, enabling/disabling, troubleshooting, and driver changes
  remain outside WinQuickSwitch.
- Cancels pending refreshes and removes the native window hook on close.
- Keeps PnP instance/container identifiers in memory and out of test output.

### Automated and visual verification

Commands:

```powershell
.\.dotnet\dotnet.exe format .\WinQuickSwitch.sln --verify-no-changes --no-restore
.\.dotnet\dotnet.exe build .\WinQuickSwitch.sln --configuration Release --no-restore
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build
.\.dotnet\dotnet.exe run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj --configuration Release --no-build -- --integration
```

Recorded results:

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 34 passed, 0 failed.
- Read-only live checks: 38 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 playback endpoints, 3 recording endpoints, 2 active sessions.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Hardware, audio, or display mutations invoked by automated verification: 0.
- Actual WPF Release window startup, full-work-area sizing, dark device table,
  nested device scrolling, and normal shutdown: passed.
- Published self-contained Portable executable startup and normal shutdown:
  passed.

Published variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 276,683 | `13207B522FB8264388124271E2D508CC99AF5AEBF8A518E655059DAD4FC290E6` |
| Portable | 64,842,797 | `3FB0000EF082206A957BAFB5CCBC5779ED695F11E5770357DE69163103BF19E6` |

Outputs:

- `artifacts/win-x64-lite/WinQuickSwitch.exe`
- `artifacts/win-x64-portable/WinQuickSwitch.exe`

### Attended M4 verification

1. Start WinQuickSwitch and confirm the Devices table fills without selecting
   **Refresh**.
2. Plug in and unplug a USB device. Confirm its row appears or disappears
   within approximately one second without selecting **Refresh**.
3. Connect and disconnect a Bluetooth device. Confirm the list refreshes and
   the UI remains responsive.
4. For a Bluetooth headset, confirm Windows media/communications profiles
   appear as one friendly device row rather than protocol-service rows.
5. Select **Bluetooth settings** and **Devices settings** and confirm each
   opens the expected Windows Settings page. Make any pairing or removal change
   in Settings, not in WinQuickSwitch.
6. Close WinQuickSwitch and confirm its process exits without a resident
   watcher.

These are attended checks because they require physical device changes and
open external Settings pages. The automated suite only reads live device state.

## 2026-07-25 - compact UI and active audio roles

### Implemented

- Added a green **DEFAULT** badge to the current console/multimedia endpoint
  and a blue **CALLS** badge to the current communications endpoint. One output
  or input can display both roles.
- Kept endpoint rows focused on the Windows friendly name; role descriptions
  are also available to screen readers and as hover tooltips.
- Renamed the endpoint actions to the shorter **Default** and **Calls** labels.
  The Calls tooltip states that applications can override the Windows role.
- Removed repeated header, display, audio, device, instruction, and footer
  sentences.
- Shortened the live audio/device summaries while retaining counts, time, and
  actionable error messages.
- Renamed Playback, Microphones, and Active application sessions to the shorter
  Output, Input, and Applications headings.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 35 passed, 0 failed.
- Read-only live checks: 39 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 output endpoints, 3 input endpoints, and 0-1 active
  applications across the verification runs.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Audio, device, or display mutations invoked by automated verification: 0.
- Actual WPF Release window visually verified with DEFAULT/CALLS badges, all
  primary sections visible, and only the long device table scrolling.
- Updated self-contained Portable executable startup and normal shutdown:
  passed.

Published variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 277,195 | `0EDDA47F343133252B2882DECD47C3E0A7C02D600B78BB3A5CB59114A3F1DB2D` |
| Portable | 64,843,029 | `76886E1E9B614F815A707B4F69C16AC05A433BDA8ADA1ED9D4519A44A5CCB6F6` |

## 2026-07-25 - compact window and native dark title bar

### Implemented

- Reduced the default window from 760 pixels to 660 pixels wide.
- Removed the duplicate in-app title so the Windows caption is the only
  application-name heading.
- Reduced page/card margins, card padding, corner radius, display-button width,
  refresh-button size, and inventory heights.
- Reduced the visually verified window height from 1,119 to 900 pixels while
  keeping Display, Audio, and Devices visible together.
- Disabled horizontal scrolling in compact tables. Long audio names use
  ellipsis and the device columns were resized to fit the narrower card.
- Added a dependency-free `DwmSetWindowAttribute` adapter for immersive dark
  framing plus app-colored border, caption, and caption text on supported
  Windows 11 builds.
- Keeps the standard Windows non-client frame and caption buttons; no custom
  chrome, hit-testing layer, background process, or UI dependency was added.
- Treats unsupported DWM attributes as a visual fallback rather than an app
  startup failure.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 36 passed, 0 failed.
- Read-only live checks: 40 passed, 0 failed.
- DWM attribute order and exact COLORREF palette mapping: passed with a fake
  native adapter; automated tests made no real window-style mutations.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 output endpoints, 3 input endpoints, 0 active applications.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Final WPF visual inspection: 660 x 900 pixels, dark Windows title bar,
  DEFAULT/CALLS badges visible, no horizontal scrollbars, and vertical
  scrolling retained for long inventories.
- Windows 10 22H2 build 19045 used its native dark-frame fallback; supported
  Windows 11 builds can additionally apply the exact caption, text, and border
  colors.
- Compact self-contained Portable executable startup and normal shutdown:
  passed.

Published compact variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 278,219 | `073BBE17B1AB10E2C1B0DAE3C92C3C41F70FBE8F1ACF04E0D61EE9ADE3156AD3` |
| Portable | 64,843,565 | `2B106EAC57F03A3FE624DAA04B1C79CF47C425B0FB45F30AFD57A2366BB0642E` |

Outputs:

- `artifacts/win-x64-lite-compact/WinQuickSwitch.exe`
- `artifacts/win-x64-portable-compact/WinQuickSwitch.exe`

The standard Lite output was open in an existing process and therefore locked.
That process was not terminated. This revision was published beside it in
versioned `-compact` folders; accidental duplicate publish folders under the
source directory were removed after the correct outputs were verified.

## 2026-07-25 - smoother display-mode transitions

### Implemented

- Replaced stateless display buttons with visually equivalent toggle controls.
- Highlights the topology reported by Windows at startup and after refresh.
- Highlights a newly requested mode immediately while the switch is running.
- Ignores requests for the already-active mode instead of relaunching
  `DisplaySwitch.exe`.
- Continues to serialize real requests by disabling the group until the active
  request completes; the selected mode remains highlighted while disabled.
- Restores the previous selection after cancellation or a reported failure.
- Added a transient `QueryDisplayConfig` settle monitor after successful
  requests. It checks every 150 ms for up to 18 reads and exits as soon as the
  requested topology appears.
- The settle monitor runs only after an explicit display request. No idle
  polling, service, or resident thread was introduced.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 39 passed, 0 failed.
- Read-only live checks: 43 passed, 0 failed.
- Immediate-settle, delayed-settle, and bounded-timeout behaviors: passed.
- Live topology: extend, 3 active displays, 3 available displays.
- Automated display-changing commands invoked: 0.
- Actual WPF visual inspection: Extend was highlighted from the live topology,
  the compact 660 x 900 layout remained intact, and normal shutdown passed.
- Updated compact Portable executable startup and normal shutdown: passed.

Published compact variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 281,803 | `BE87CA174002D1099F225E32528DC9E24A9468AF91A60E943D0D9CECE33AAE29` |
| Portable | 64,844,832 | `C0EEDF5B8B2CF84011FAAEC62DB7E5940F4797EA1E4C3487C85683FF3903632B` |

### Attended display-transition check

1. Start WinQuickSwitch and confirm the currently active display mode is blue.
2. Select **Duplicate** or **Extend** once. Confirm the requested mode turns
   blue immediately and remains selected while Windows changes the displays.
3. After the displays settle, confirm the status and blue selection agree.
4. Select the already-active mode again. Confirm there is no new display
   blanking or rearrangement.
5. For **PC screen only** and **Second screen only**, select **No** in the
   warning and confirm the original mode remains selected.
6. Run the accepted risky-mode checks only when it is safe for one display to
   turn off, then restore the original topology.

Physical blanking during a real topology change cannot be removed by the app;
it is controlled by Windows, the graphics driver, and monitor link training.

## 2026-07-25 - resident widget foundation

### Implemented

- Replaced the all-sections window with a 500-pixel, content-sized widget that
  shows Display, Audio, or Devices one panel at a time.
- Added the fixed `Win + Shift + Q` global show/hide shortcut through
  `RegisterHotKey` with no-repeat behavior.
- Added direct keyboard access: `Ctrl + 1/2/3` selects panels, `1` through `4`
  select display modes, `O/I/A` focuses the audio lists, and `Esc` hides.
- Added pointer-aware, multi-monitor placement that opens beside the pointer,
  flips away from right/bottom edges, handles negative monitor coordinates,
  and clamps inside the current work area.
- Added a single-instance mutex plus activation event. A second launch reveals
  the resident instance and exits.
- Changed the close button and click-away behavior to hide the widget. The
  visible **Quit** action remains the explicit way to end the process.
- Added a short foreground-activation grace so startup and second-instance
  handoff are not immediately hidden by a stale deactivation event.
- Audio notifications now start only while Audio is visible and stop when the
  panel is left or the widget hides. Device events received while hidden defer
  enumeration until Devices is opened.
- Added unit coverage for normal, edge-flipped, and negative-coordinate widget
  placement and expanded the live watcher test to cover stop and restart.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 42 passed, 0 failed.
- Read-only live checks: 46 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 output endpoints, 3 input endpoints, 1 active application.
- Live audio watcher start, stop, restart, and final disposal: passed.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Resident lifecycle: registered hotkey revealed the hidden widget; toggle
  handling hid it; a second launch exited and revealed the first process.
- Visual inspection: 500-pixel Display panel, dark native title bar, compact
  navigation, current Extend highlight, and no clipping.
- Lite and Portable widget executables both started and remained resident until
  the exact test process was stopped.
- Automated display-changing commands invoked: 0.

Published widget variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 296,139 | `FA4FAD20782B3E55096ADEBD8A79823525566D290002607DCB9F32828864C68A` |
| Portable | 64,851,065 | `118031F42F8A1E70D3E0C0BCAE7BD1C347B856A81E6D45066F7D577557440DDE` |

Outputs:

- `artifacts/win-x64-lite-widget/WinQuickSwitch.exe`
- `artifacts/win-x64-portable-widget/WinQuickSwitch.exe`

### Remaining resident work

- Add optional start-with-Windows behavior.
- Decide whether a tray icon adds enough value beyond the hotkey and visible
  Quit action.
- Complete high-contrast, screen-reader, and 100-200% DPI validation.

## 2026-07-25 - stable placement, configurable shortcuts, and themes

### Implemented

- Removed the repeated pointer-relative placement calls from reveal and panel
  changes. The first reveal still uses the pointer's monitor work area; later
  hide/show cycles retain the last window position.
- Added an Options panel and `Ctrl + 4` panel shortcut.
- Added editable global shortcuts for show/hide and direct Display, Audio, and
  Devices access. A shortcut requires Ctrl, Alt, or Win plus A-Z, 0-9, or
  F1-F12; Shift is optional.
- Added duplicate detection and isolated native registration failures. A
  conflicting direct-panel binding does not disable the show/hide binding or
  other successful shortcuts.
- Added a saved dark/light toggle. Dynamic WPF color resources update existing
  controls immediately, shortcut fields use the app palette, and the native
  title bar receives the matching DWM colors plus a frame refresh.
- Added dependency-free JSON persistence at
  `%LOCALAPPDATA%\WinQuickSwitch\settings.json` with atomic replacement.
- Added an explicit test-instance identifier for isolated UI/package checks;
  normal launches continue to use the same per-user singleton names.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 48 passed, 0 failed.
- Read-only live checks: 52 passed, 0 failed.
- Live topology: extend, 2 active displays, 2 available displays.
- Live audio: 6 output endpoints, 3 input endpoints, 0 active applications.
- Live audio watcher start, stop, restart, and final disposal: passed.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Stable-position UI check: the widget remained at `674,434` across hide and
  second-launch reveal.
- Direct-panel UI check: a temporary `Ctrl + Alt + F12` binding revealed the
  hidden widget with Audio active.
- Visual checks: dark Options fields matched the dark palette; light mode
  updated the window, controls, selection state, and native title bar.
- All temporary UI-test settings were removed after verification. A previously
  running widget process was not stopped or overwritten.
- Lite and Portable options builds both started and remained resident until
  their exact isolated test process was stopped.
- Automated display-changing commands invoked: 0.

Published options variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 314,059 | `30FF09811B6C55F6D00F20E299518357A7DB6D5304E6B469A866F483AFE1AADA` |
| Portable | 64,858,466 | `86DD123ED7ED8CA4C0B8EAD09A05DFD71356131A2AD769C531263194B5403CB6` |

Outputs:

- `artifacts/win-x64-lite-options/WinQuickSwitch.exe`
- `artifacts/win-x64-portable-options/WinQuickSwitch.exe`

## 2026-07-27 - optional start with Windows

### Implemented

- Added **Start WinQuickSwitch when I sign in** to Options.
- Added isolated per-user startup registration through
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The value contains the
  quoted current executable path plus the allowlisted `--startup` argument and
  does not require elevation.
- Startup launches now create the native window handle, register global
  hotkeys, and remain resident without showing the widget.
- A normal later launch reveals that hidden resident instance. A delayed
  Windows startup launch does not reveal an instance that is already running.
- Kept startup registration separate from JSON preferences so the checkbox
  reflects Windows' current registration and identifies a stale executable
  path as disabled.
- Kept registration errors nonfatal, restored the checkbox to the actual
  Windows state on failure, and reported the error in Options.
- Documented enabling, disabling, moving, and deleting the executable.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 52 passed, 0 failed.
- Read-only live checks: 56 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 output endpoints, 3 input endpoints, 1 active application.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Hidden-start lifecycle: the native widget window existed but was not visible;
  a normal second launch exited and revealed the original window.
- Lite and Portable package checks both passed hidden startup and later reveal.
- Options UI check: the startup checkbox was present, visible, and not clipped.
- The user's actual startup registration was absent and was not changed during
  verification. Registry mutations were tested through an isolated fake.
- Existing user widget processes and previous artifact folders were not
  stopped or overwritten.
- Automated display-changing commands invoked: 0.

Published startup-option variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 318,667 | `647071B8A41CFA0C4C0590B28E66B7CEEA34E293C900FACBF2977642600477EC` |
| Portable | 64,860,289 | `B69E9B8BB4C6B85900DDF2B83A81B56D33977CD5B0C55D1EB3FF81794A62899E` |

Outputs:

- `artifacts/win-x64-lite-startup/WinQuickSwitch.exe`
- `artifacts/win-x64-portable-startup/WinQuickSwitch.exe`

## 2026-07-29 - favorite outputs and direct display hotkeys

### Implemented

- Added four optional favorite playback-output slots. Audio's compact
  **Favorite** action adds the selected output or removes it when selected
  again.
- Persisted each favorite by its Core Audio endpoint ID, last friendly name,
  and optional shortcut. Invalid and duplicate endpoint entries normalize away,
  and existing settings files migrate with all new fields empty.
- Added a collapsed **Favorite output shortcuts** section to Options. Each
  saved output receives its own editable global shortcut.
- Added a collapsed **Display mode shortcuts** section for PC screen only,
  Duplicate, Extend, and second screen only.
- Kept Duplicate and Extend shortcuts unobtrusive while hidden. PC-screen-only
  and second-screen-only reveal Display and retain the existing confirmation
  because they can turn off the current screen.
- Routed favorite shortcuts through the existing general-default Core Audio
  adapter. A failed selection reveals Audio with the error; a successful
  selection stays hidden.
- Expanded duplicate-chord detection and conflict-isolated native registration
  across all panel, display-mode, and favorite-output actions.
- Changed panel tabs to respond to the native accessibility toggle action as
  well as pointer input.
- Added deterministic size-change clamping. A panel keeps its current position
  when it fits and moves only by the overflow amount when taller content would
  cross a monitor work-area edge.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 57 passed, 0 failed.
- Read-only live checks: 61 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 7 output endpoints, 3 input endpoints, 1 active application.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Settings checks covered old-file migration, favorite persistence, duplicate
  endpoint cleanup, shortcut reset with favorites retained, display/favorite
  action mapping, and conflict-isolated global registration.
- Accessibility-driven UI checks confirmed all four display shortcut fields,
  favorite-output guidance, and Audio's Favorite control and summary were
  visible without clipping.
- The UI check found and verified the fix for taller panels crossing a monitor
  edge; a pure placement test confirms in-bounds positions do not move.
- Lite and Portable package checks both passed hidden startup and later reveal.
- Existing settings, default audio endpoints, display topology, running user
  widget processes, and previous artifact folders were not changed.
- Automated display-changing commands invoked: 0.
- Automated audio-endpoint changes invoked: 0.

Published favorite-hotkey variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 335,051 | `A9137CCCE7A249574126650A6C164E34BA6B4F03D3B90B2D5FBBA4AFA7AA0F8F` |
| Portable | 64,865,717 | `C9AAD8B7EF334EBB64E7FD38415514C59CEC9D8ECC53B764765EA682FE397EE4` |

Outputs:

- `artifacts/win-x64-lite-favorites/WinQuickSwitch.exe`
- `artifacts/win-x64-portable-favorites/WinQuickSwitch.exe`

## 2026-07-30 - direct display switching and general Sound settings

### Implemented

- Replaced the `DisplaySwitch.exe` helper-process launch with a direct
  `SetDisplayConfig` call for PC screen only, Duplicate, Extend, and second
  screen only.
- Kept display changes off the WPF UI thread, preserving the immediate selected
  state and the short topology-settle monitor so the widget remains responsive.
- Removed the obsolete hidden-process display adapter.
- Added a compact **Sound settings** action to Audio. It opens the general
  Windows **System > Sound** page through `ms-settings:sound`; it does not jump
  directly to Volume mixer or a specific endpoint page.
- Found that the running resident and the Windows startup entry still pointed
  to the older `win-x64-lite-startup` artifact. Published the new fast builds,
  stopped only that verified old resident process, started the new Lite build
  hidden, and moved the startup entry to the same executable.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 57 passed, 0 failed.
- Read-only live checks: 61 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 6 output endpoints, 3 input endpoints, 1 active application.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Tests cover all four `SetDisplayConfig` topology mappings, native errors,
  cancellation, unknown modes, and execution away from the calling thread.
- The existing Sound-settings test verifies the exact `ms-settings:sound` URI.
- UI automation confirmed the new Sound-settings action is visible and not
  clipped.
- Lite and Portable package checks both passed hidden startup and later reveal.
- Final live-state verification found exactly one resident process running from
  `artifacts/win-x64-lite-fast`, with the Windows startup entry pointing to
  that same executable with `--startup`.
- Automated display-changing commands invoked: 0.
- Automated audio-endpoint changes invoked: 0.

Published fast-display variants:

| Variant | Bytes | SHA-256 |
| --- | ---: | --- |
| Lite | 334,539 | `76DC17ADA60EBEB2A2633EB3BA08C7B7D14CE15CEEDEA5CF65FD28CDCE1F142B` |
| Portable | 64,865,537 | `B7DC60F40B6F030DBE15F2AA2A65098BCC3361E36041E9F2D5E6313C78733CED` |

Outputs:

- `artifacts/win-x64-lite-fast/WinQuickSwitch.exe`
- `artifacts/win-x64-portable-fast/WinQuickSwitch.exe`

## 2026-07-30 - immediate display-mode actions

### Implemented

- Removed the confirmation dialog from all four display-mode actions.
- Display buttons and their global shortcuts now call the display service
  immediately; shortcuts can remain hidden instead of revealing the widget for
  a prompt.
- Simplified deactivation handling now that display confirmation no longer
  temporarily suppresses widget auto-hide.
- Updated the README, architecture notes, and product plan to describe the
  immediate behavior.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Regression suite: 57/57 tests passed.
- Removed all display-confirmation code paths and prompt strings from the
  application.
- The existing display-service and topology-transition tests remain unchanged;
  they continue to cover the native call, failure handling, cancellation, and
  settle monitoring.
- Republished and restarted the hidden Lite resident. Final live-state
  verification found exactly one process running from
  `artifacts/win-x64-lite-fast`, with the Windows startup entry pointing to
  that same executable with `--startup`.
- Automated display-changing commands invoked: 0.

Published immediate-action variants:

| Variant | SHA-256 |
| --- | --- |
| Lite | `FFEE84E8E87BF1C5FA79F82590118561333A211A9617D3A6B72880EAC4D7257F` |
| Portable | `9CDE5E7D2A8F2EBDFCD1DF198A6A6EE89D883EFB6579F4ADA58076B88A3FC49D` |

## 2026-07-30 - explicit shortcut Unset controls

### Implemented

- Added an **Unset** button beside every built-in display, panel, and
  show/hide shortcut.
- Added the same button to each saved favorite-output shortcut.
- Reused the existing conflict-safe shortcut registration and persistence path,
  so unsetting immediately unregisters the global hotkey and saves the empty
  value. Delete/backspace in a focused shortcut field still works.
- Updated the Options guidance and architecture/product documentation.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Regression suite: 57/57 tests passed.
- No display or audio changes were invoked by automated verification.

## 2026-07-31 - Windows Volume mixer entry point

### Implemented

- Added a compact **Volume mixer** action to the Audio header using the
  allowlisted `ms-settings:apps-volume` URI.
- Kept the general **Sound settings** action separate at `ms-settings:sound`.
- Renamed the in-widget Applications heading to **App volumes**, clarifying
  that its existing live Core Audio sliders and mute controls are the quick
  mixer while Windows Settings provides the full fallback.
- Routed the new Settings action through `IDefaultAudioEndpointService` and the
  existing Windows launcher boundary; no new resident watcher or dependency was
  added.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 58 passed, 0 failed.
- Read-only live checks: 62 passed, 0 failed.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 4 playback endpoints, 3 recording endpoints, 0 active sessions.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Added an isolated test for the exact Windows Volume mixer URI.
- No audio state is changed when the Settings action is used.
- Published both fast variants and restarted the hidden Lite resident. Final
  verification found exactly one resident process, and the Windows startup
  entry points to that same Lite executable with `--startup`.

Published Volume mixer variants:

| Variant | SHA-256 |
| --- | --- |
| Lite | `5B1EA6CFCE9411DA652DF5B2D727FE1356BADF315B0FE2A1E15D439DB4F2809C` |
| Portable | `6BDDE199D9B90EB7B6ACCB8517E0A02FAFB18074BEF4E383856235B067D609D6` |

## 2026-07-31 - Wi-Fi and Bluetooth quick controls

### Implemented

- Added independent Wi-Fi and Bluetooth state controls to Devices using
  `Windows.Devices.Radios`.
- Reads radio state without requesting permission. Permission is requested only
  after a user clicks a toggle; a denied or unavailable change opens the
  matching Windows Settings page.
- Added allowlisted Wi-Fi, Bluetooth, Airplane mode, Network, and connected
  device Settings actions.
- Refreshes radio state when Devices opens, after a requested state change, and
  on manual refresh. No polling thread, service, elevation, PnP disable, or
  shell command was added.
- Updated the Windows target to Windows 11 version 22H2 (`22621`) so the .NET
  build consumes the supported WinRT radio projection directly.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 62 passed, 0 failed.
- Read-only live checks: 67 passed, 0 failed.
- Live radio state: Wi-Fi on, Bluetooth on.
- Live topology: extend, 3 active displays, 3 available displays.
- Live audio: 4 playback endpoints, 3 recording endpoints, 1 active session.
- Live normalized device inventory: 13 Bluetooth, 8 wired.
- Computer-use automation could not attach to the WPF window in two attempts,
  so this milestone does not claim an automated visual-layout pass.
- Actual radio state-change requests invoked by automated verification: 0.
- After the Windows target change, an explicit runtime-specific restore
  refreshed the `win-x64` assets before publishing both
  variants.
- Final resident-state verification found exactly one hidden Lite process, and
  the Windows startup entry points to that same executable with `--startup`.

Published wireless-control variants:

| Variant | SHA-256 |
| --- | --- |
| Lite | `6099E3B7083473F2EFEF4E3CECCCBF7A502D635FE76F384563D305A8CDFECA92` |
| Portable | `6534610F8C5F184B846A85175A2025C11ABDA5E996F6199E9A1A941B7C42F963` |

## 2026-08-01 - native notification-area tray icon

### Implemented

- Added a native `Shell_NotifyIcon` integration on the existing WPF window
  handle, keeping the resident app lightweight and dependency-free.
- Left-clicking the icon or choosing **Open WinQuickSwitch** reveals the widget.
  Right-clicking exposes **Open WinQuickSwitch** and **Exit WinQuickSwitch**.
- Closing, pressing `Esc`, or clicking away still hides the widget to the tray;
  the explicit Quit action and the tray Exit action terminate the process.
- Tray registration is best-effort and is removed during window shutdown. If the
  Windows shell cannot register the icon, the resident hotkey and widget remain
  available.

### Verification

- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 62 passed, 0 failed.
- Read-only live checks: 67 passed, 0 failed.
- Published and launched the Lite build from the resident fast artifact path;
  one responsive process was observed.
- Computer-use tray/window automation could not attach to the hidden WPF window
  after two launch attempts, so no automated claim is made about clicking the
  tray menu. The native registration path is covered by the successful build
  and the running resident process; perform one attended click check on the
  notification-area icon after updating.
- No display, audio, radio, or device state changes were invoked.

Published tray-enabled variants:

| Variant | SHA-256 |
| --- | --- |
| Lite | `A89967D7EB93B89CBAEC979FCE7EED338FA984299DCD6E0C63D89AF4FC8DD11E` |
| Portable | `EA7E735451ABF98603C5CA0FCD1287F2B2369879BBBAB0565A57FB70F71D63B5` |

## 2026-08-01 - taskbar visibility and settings shortcuts

### Implemented

- Added a compact Taskbar section to Options with explicit **Show taskbar** and
  **Hide taskbar** actions. Hide means Windows auto-hide: the taskbar remains
  available when the pointer reaches its screen edge.
- Reads the current taskbar state with `SHAppBarMessage(ABM_GETSTATE)` and
  applies changes with `ABM_SETSTATE`; shell/API failures remain nonfatal and
  are shown in the status line.
- Added Settings shortcuts for Taskbar, Display, and Notifications using the
  documented `ms-settings:` URIs. Broader taskbar behavior remains owned by
  Windows Settings.

### Verification

- Format verification: passed with no changes required.
- Release build: passed with 0 warnings and 0 errors.
- Isolated checks: 66 passed, 0 failed.
- Read-only live checks: 68 passed, 0 failed.
- Live taskbar state: visible.
- Taskbar state-changing calls were exercised only against a fake backend;
  no real taskbar visibility was changed by automated verification.
- No display, audio, radio, or device state changes were invoked.
- Published both fast variants and restarted the hidden Lite resident. Final
  verification found one responsive process at the expected Lite path.

Published taskbar-control variants:

| Variant | SHA-256 |
| --- | --- |
| Lite | `8E1E4580986A400D02766EA12EE81441FB4D99A0A68724467F4A01584D222623` |
| Portable | `31717007932B61B9FFFAC952C3C8E913A498BBF2A195C9DC32B5B630CEF1EDFE` |

## 2026-08-07 - favorites and profiles milestone

### Implemented

- Added four playback and four microphone favorites with aliases, ordering,
  explicit removal, role selection, and disconnected-device persistence.
- Added profile capture, editing, duplication, pinning, application, deletion,
  partial-success warnings, and in-session undo.
- Added profile playback/microphone favorite selectors and optional master
  volume and microphone mute state.
- Added role-aware favorites to the tray menu.
- Added optional profile global shortcuts with collision-safe dynamic IDs.

### Verification

- Release build: passed with 0 warnings and 0 errors.
- Automated checks: 73 passed, 0 failed.
- Source diff check: passed.
- Published both Lite and Portable EXE variants from the verified Release build.

Published favorites-and-profiles variants:

| Variant | SHA-256 | Size |
| --- | --- | ---: |
| Lite | `36FD1A603087ED897732F684A94E528863AFC266FAD15DD49D314BFAB5E7BA89` | 27,052,010 bytes |
| Portable | `D8E7D97A1B6C2E3B19A7466A6563FDC7D72FF3E0555832945C3D53F9AF3B1C65` | 71,518,418 bytes |

## 2026-08-08 - About and sharing disclaimer

### Implemented

- Added an About page to the main navigation.
- Added a clear warning not to copy, upload, redistribute, or publish the
  application without permission.
- Added privacy guidance for local settings, saved profiles, device names,
  and shortcuts.
- Added responsible-use guidance because the app can change Windows display,
  audio, wireless, and taskbar settings.
- Added `Ctrl + 6` as a keyboard shortcut for the About page.
- Tightened the tab sizing so all six navigation tabs fit in the compact shell.

### Verification

- Release build: passed with 0 warnings and 0 errors.
- Automated checks: 73 passed, 0 failed.
- Published the updated Lite EXE.

Published About/disclaimer variant:

| Variant | SHA-256 | Size |
| --- | --- | ---: |
| Lite | `0CC95494E2073D6EBEE206E2AA39A58449D60189F9696703299E87A79F51CA7B` | 27,052,010 bytes |

## 2026-08-08 - commercial licensing milestone

### Implemented

- Added a proprietary repository and source-code notice in `LICENSE.txt`.
- Added a customer-facing Microsoft Store EULA covering the license grant,
  permitted personal and internal business use, redistribution restrictions,
  Store terms, updates, privacy boundaries, termination, warranties, liability,
  consumer rights, and British Columbia governing law.
- Left explicit publisher legal-name and support-contact placeholders that must
  be completed before Store submission.
- Updated the About page from private-build wording to commercial licensed-use
  wording that permits Store-managed multi-device installation while prohibiting
  unauthorized sharing, resale, sublicensing, and redistribution.
- Added licensing guidance to the repository README.
- Added a Store licensing guide covering MSIX distribution, purchase
  entitlement, optional runtime checks, EULA acceptance, and remaining work.

### Verification

- Release build: passed with 0 warnings and 0 errors.
- Automated checks: 73 passed, 0 failed.
- Published and restarted the updated Lite EXE.

| Variant | SHA-256 | Size |
| --- | --- | ---: |
| Lite | `78C4328A2240454EFC73176E4D30694F381A793548771458186C081894A6DAB5` | 27,052,010 bytes |

## 2026-08-08 - publisher identity and in-app legal documents

### Implemented

- Set the legal publisher to Andrew Chang and the support contact to
  `andrew.chang0@outlook.com` in the source notice and EULA.
- Versioned the EULA as 1.0 and removed publication placeholders.
- Added a privacy policy describing local settings, profiles, device data,
  startup registration, retention, deletion, and Microsoft Store boundaries.
- Embedded the EULA and privacy policy into the application assembly.
- Added themed, selectable, scrollable in-app document viewers from About.
- Recorded a one-time paid model, target price near CAD $1.00, no trial, and no
  mandatory first-run EULA screen for the first Store release.
- Added automated checks for embedded publisher and support details and for
  readable legal-document rendering.

### Verification

- Release build: passed with 0 warnings and 0 errors.
- Automated checks: 75 passed, 0 failed.
- Published and restarted the updated Lite EXE.

| Variant | SHA-256 | Size |
| --- | --- | ---: |
| Lite | `72919E213592CD34BC8A548F8CF00F4E780D5B8CA4EB5151C535F322D90B05D9` | 27,068,394 bytes |

## 2026-08-08 - Microsoft Store package identity and MSIX build

### Implemented

- Recorded the reserved Partner Center identity `Dreamle.WinQuickSwitch`,
  publisher `CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8`, and publisher display
  name `Dreamle` in the package manifest.
- Added an x64 packaged-classic-app manifest with medium-integrity full-trust
  execution for the existing WPF desktop application.
- Added deterministic placeholder package logos and their generation script.
- Added a reproducible Store build that publishes the self-contained Release
  application, creates an unsigned MSIX with the Windows SDK, and wraps it in
  a `.msixupload` archive for Partner Center.
- Documented package versioning, local signing boundaries, and remaining Store
  certification work.

### Verification

- Unpacked the MSIX and verified the manifest identity, publisher, version,
  architecture, executable, and all declared logo files.
- Verified that the `.msixupload` archive contains exactly the generated MSIX.
- Confirmed that the local MSIX is unsigned, as expected for this Store upload
  candidate; Microsoft Store supplies the production signature after acceptance.
- Release build: passed with 0 warnings and 0 errors.
- Automated checks: 75 passed, 0 failed.
- Published the updated Lite EXE.

Store package candidate (version `1.0.1.0`):

| Artifact | SHA-256 | Size |
| --- | --- | ---: |
| MSIX | `BDB4674AECCEFE2DD969592B7D2E3DB1DD239C0EF1A506A8858B9C666E5C1460` | 70,945,063 bytes |
| MSIX upload | `ED3A993B1C4C03F2B6ADB4A4BBA99E020583F546A5E10A77CC2E74C6071C7A48` | 70,324,606 bytes |

Republished desktop executable:

| Variant | SHA-256 | Size |
| --- | --- | ---: |
| Lite | `351DFBF6294DD540462079FBD8529BF2443D017B665DA73F6AB0E3F9CEA3AA77` | 27,068,394 bytes |

Remaining before submission: replace placeholder artwork, migrate sign-in
startup to a package-aware mechanism, host the EULA and privacy policy, and
complete signed/private-flight and Windows App Certification Kit testing.

## 2026-08-08 - Partner Center package identity correction

### Corrected

- Replaced the rejected `Dreamle.MSQuickSwitch` package name with Partner
  Center's reserved `Dreamle.WinQuickSwitch` identity.
- Recorded the expected package family name
  `Dreamle.WinQuickSwitch_sth8w7gs4yt8p`.
- Rebuilt the x64 MSIX and `.msixupload` candidate at version `1.0.0.0`.

### Verification

- Package manifest identity: `Dreamle.WinQuickSwitch`.
- Package publisher: `CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8`.
- Upload archive: exactly one `WinQuickSwitch_1.0.0.0_x64.msix` entry whose
  uncompressed size matches the rebuilt package.
- Release build: passed with 0 warnings and 0 errors.
- Automated checks: 75 passed, 0 failed.

## 2026-08-08 - uniquely versioned Partner Center replacement

### Corrected

- Incremented the replacement package from `1.0.0.0` to `1.0.0.1` so its full
  name cannot conflict with either previously uploaded package.
- Changed `.msixupload` output names to include the package version and
  architecture, preventing the old browser upload from being mistaken for the
  replacement.
- Moved older local Store artifacts into the ignored `obsolete` folder, leaving
  only the `1.0.0.1` MSIX and upload at the artifact root.

### Verification

- Inspected `AppxManifest.xml` inside the MSIX inside the final `.msixupload`.
- Embedded identity: `Dreamle.WinQuickSwitch`, version `1.0.0.1`, x64.
- Expected full name:
  `Dreamle.WinQuickSwitch_1.0.0.1_x64__sth8w7gs4yt8p`.
- Expected family name: `Dreamle.WinQuickSwitch_sth8w7gs4yt8p`.
- Upload contains exactly one MSIX and passes the guarded package build.

## 2026-08-08 - Microsoft Store revision-number correction

### Corrected

- Partner Center reported that Store app packages cannot use a nonzero fourth
  version component, making `1.0.0.1` invalid.
- Incremented the third component and rebuilt the replacement as `1.0.1.0`.
- Added a build-time rule that rejects nonzero fourth components and version
  components above the MSIX limit of 65535.
- Moved the rejected `1.0.0.1` artifacts out of the active upload folder.

### Verification

- Confirmed that the build rejects `1.0.0.1` before creating artifacts.
- Inspected the manifest nested inside the final `1.0.1.0` `.msixupload`.
- Embedded identity: `Dreamle.WinQuickSwitch`, version `1.0.1.0`, x64.
- Expected full name:
  `Dreamle.WinQuickSwitch_1.0.1.0_x64__sth8w7gs4yt8p`.
- Expected family name: `Dreamle.WinQuickSwitch_sth8w7gs4yt8p`.
- Upload contains exactly one MSIX and the guarded package build passed.

## 2026-08-10 - Windows 10 Store compatibility update

### Implemented

- Kept the compile-time Windows SDK surface at `10.0.22621.0` and added
  `SupportedOSPlatformVersion` `10.0.19041.0` to the application and test
  projects so compatibility analysis uses the Windows 10 runtime floor.
- Lowered the MSIX `Windows.Desktop` minimum from build `22621` to build
  `19041`, allowing installation on Windows 10 version 2004 and newer.
- Incremented the Store package version to `1.0.2.0` and updated the package
  builder's default version.
- Updated the repository and Store-listing documentation to describe Windows
  10 and Windows 11 compatibility.

### Verification

- Windows 10 host used for verification: version 22H2, build 19045, x64.
- Release solution build: 0 warnings and 0 errors.
- Isolated checks: 75 passed, 0 failed.
- Read-only live checks: 81 passed, 0 failed, including display topology,
  Core Audio, connected-device inventory, wireless radio state, and taskbar
  state on Windows 10.
- The repository-wide format verification remains blocked by pre-existing
  mixed line endings in unrelated source files; no bulk line-ending rewrite
  was included in this compatibility change.
- Created `WinQuickSwitch_1.0.2.0_x64.msix` and
  `WinQuickSwitch_1.0.2.0_x64.msixupload`.
- Inspected the manifest nested in the final upload: identity
  `Dreamle.WinQuickSwitch`, version `1.0.2.0`, x64, minimum build `19041`,
  `packagedClassicApp`, `mediumIL`, and `runFullTrust`.
- Confirmed that the upload's nested MSIX SHA-256 matches the standalone MSIX.
- MSIX SHA-256:
  `2AC8816F38065049B5B33098D6D45532478392983B9B8C2437B056B340428500`.
- MSIXUPLOAD SHA-256:
  `10B6ABB78535B64FA740A2D6A811E1F21A5A8C435AE20FD8EBAC803D7920B10D`.

## 2026-08-10 - resizable Windows-aligned widget shell

### Implemented

- Replaced the fixed 500-pixel, content-sized window with a user-resizable
  720 x 640 default shell and a 620 x 440 minimum viewport.
- Preserved native Windows caption, system menu, borders, and resizing while
  retaining the resident widget's always-on-top and click-away behavior.
- Added vertical overflow scrolling for reduced window heights and disabled
  horizontal overflow so the complete six-tab navigation remains available.
- Applied Segoe UI display text, layout rounding, Windows 11-style corner
  radii, neutral Windows surfaces, and the standard Windows blue selection
  color to both dark and light palettes.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Parsed the compiled XAML input and confirmed a 720 x 640 default, 620 x 440
  minimum, native `CanResize` frame, all six navigation tabs, disabled
  horizontal overflow, and automatic vertical scrolling.
- Isolated checks: 75 passed, 0 failed.
- Read-only live checks: 81 passed, 0 failed, including display topology,
  Core Audio, connected-device inventory, wireless radio state, and taskbar
  state on Windows 10.
- Rebuilt the guarded `1.0.2.0` x64 MSIX and `.msixupload` with the responsive
  shell. The Microsoft Store will sign the accepted submission.
- Rebuilt MSIX SHA-256:
  `39A8AE319F9C4C3A47F9E19AE61B1F75AEAD91633E372C61FA71F4465C222C01`.
- Rebuilt MSIXUPLOAD SHA-256:
  `0D913B0E61F3ACB5D5454FA748FFA9B08AC553A9C270305D89E76D30059EDBA1`.

## 2026-08-10 - larger default window and content lists

### Implemented

- Increased the default widget size from 720 x 640 to 900 x 720 and the
  minimum resize boundary from 620 x 440 to 720 x 520.
- Capped both width and height to the current Windows working area so the
  larger default remains usable on smaller displays.
- Increased the visible playback, recording, app-volume, and connected-device
  list areas so the larger shell shows more useful content.
- Incremented the Store package to `1.0.3.0` so it cannot be confused with or
  rejected as a duplicate of the earlier `1.0.2.0` build.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Read-only live checks: 81 passed, 0 failed.
- Parsed the XAML layout contract and confirmed the 900 x 720 default,
  720 x 520 minimum, working-area caps, native resizing, six tabs, automatic
  vertical overflow, and 230-pixel connected-device list allowance.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.3.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `BE32940A26320C27C1B9F97163088DE89B79467DA4A3ED4DDF9EC83E5C3A1C67`.
- MSIXUPLOAD SHA-256:
  `3F6308ABAAE49EA88FA47E5B8474DD2CD23E57AE9EE149A9F1C8A8B38A2947E4`.

## 2026-08-10 - native window controls and flexible resizing

### Implemented

- Enabled the normal Windows taskbar presence for the visible application so
  the native title bar exposes minimize and maximize/restore alongside Close.
- Preserved manual edge and corner resizing through the Windows-managed frame,
  with a more flexible 600 x 420 minimum and automatic overflow scrolling.
- Prevented the resident click-away behavior from hiding a minimized window,
  keeping it available for restoration from the taskbar.
- Restored a minimized window when it is explicitly opened through the tray,
  shortcut, or another launch request.
- Incremented the Store package to `1.0.4.0` for this window-management update.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Read-only live checks: 81 passed, 0 failed.
- Parsed the final XAML and lifecycle code to confirm `CanResize`, taskbar
  presence, 900 x 720 default, 600 x 420 minimum, vertical overflow, and the
  minimized-window auto-hide guard.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.4.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `2B3FFAEEAAE84A04D8D00E7CC8F9EA4CD25E7928B95E9C1EC28A667E0C98B8E0`.
- MSIXUPLOAD SHA-256:
  `B2D6D088B0937B5088A6C0E02F9CF65C4EB6BA628915BD3E19E5BB8CFDD72BB4`.
- Lite EXE SHA-256:
  `1CB0F8F4CE55AB0AFDA8697B7659BCCC8423AD900F5C12D989FFE86C8CCCF8B3`.

## 2026-08-10 - responsive active-panel fill

### Implemented

- Moved navigation outside the panel scroller and introduced a star-sized
  active-panel host that follows the window's available width and height.
- Kept each active panel independently vertically scrollable when its content
  exceeds a manually reduced window size.
- Converted Devices to a row-based layout so the connected-device table grows
  and shrinks with the active panel instead of retaining a fixed maximum.
- Converted Audio to a row-based layout so endpoint and app-volume lists share
  and consume the available panel height.
- Incremented the Store package to `1.0.5.0` for the responsive-panel update.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Read-only live checks: 81 passed, 0 failed.
- Parsed the final XAML layout contract and confirmed a star-sized window
  content row, viewport-height panel host, flexible Devices row, no fixed
  Devices list maximum, and two flexible Audio rows.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.5.0`, minimum Windows build `19041`.
- The standard Lite output was locked by the user's running process, so the
  refreshed executable was published without disruption to
  `artifacts/win-x64-lite-1.0.5.0`.
- MSIX SHA-256:
  `99FCDEF3792CB117049E957200648AC96C4980D54C4707E8C60A55EE3744A306`.
- MSIXUPLOAD SHA-256:
  `CF30BD8950E7F679DB62B6A8AF538C42AA42285F5F77B10871F3CF89F61ADF73`.
- Lite EXE SHA-256:
  `341B6449AA6AC32E1AD264DB59FB9E873A932B456F43414AFEACBB755807A473`.

## 2026-08-13 - refresh display choices after monitor changes

### Implemented

- Added handling for Windows `WM_DISPLAYCHANGE` and hardware-device change
  notifications while the Display panel is visible.
- Debounced the notifications for 250 ms so monitor hot-plug or re-enumeration
  bursts produce one settled topology read instead of transient intermediate
  states.
- Reuses the existing topology snapshot application, updating the status,
  selected mode, and Duplicate/Extend availability as soon as the new monitor
  is detected.
- Incremented the Store package to `1.0.6.0` for the monitor-refresh update.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Read-only live checks: 81 passed, 0 failed, including the current display
  topology and attached-device inventory.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.6.0`, minimum Windows build `19041`.
- The refreshed Lite executable was published to the versioned folder
  `artifacts/win-x64-lite-1.0.6.0` so any currently running Lite copy was not
  interrupted.
- MSIX SHA-256:
  `A78A36E08EC38CCA89752FA20872F231CC677E2ABF4FACC90B6AB3CA02D103B7`.
- MSIXUPLOAD SHA-256:
  `146B0C529F3537D5DFAA23719DD7BC842F3ED5A6F946BABF338DA3919ED2CDE2`.
- Lite EXE SHA-256:
  `055714C58C87B4CB9C15C5F20909A9578F070F0CDB540F241AB6F250CEF38F14`.

## 2026-08-13 - settle display topology after mode changes

### Implemented

- Kept polling after Windows first reports the requested display mode, requiring
  three identical topology snapshots before updating the Display panel.
- This covers the `Second screen only` to `Extend` transition, where Windows can
  report `Extend` while the third monitor is still activating.
- Added a regression test for the intermediate two-display snapshot followed by
  the settled three-display topology.
- Incremented the Store package to `1.0.7.0` for the settled-topology update.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and live checks: 82 passed, 0 failed.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.7.0`, minimum Windows build `19041`.
- The refreshed Lite executable was published to the versioned folder
  `artifacts/win-x64-lite-1.0.7.0`.
- MSIX SHA-256:
  `700CEEA83AA2B379030929DF0B225635AB9E0D98F4C9A646398C617FA258D7A4`.
- MSIXUPLOAD SHA-256:
  `8E4B421E14FF389A7145C84AF0146E458CA61DF7AB2B8DB65127E71A2A9DDFF8`.
- Lite EXE SHA-256:
  `1F87A52F4B62D41866B548836559E36B6E7DA97E4C6DF51D1BE898677B8B1467`.

## 2026-08-13 - activate every available display in Extend

### Implemented

- Corrected the completion rule for Extend: an `Extend` snapshot is no longer
  accepted when Windows reports fewer active displays than available displays.
- Removed the early-return path that prevented another Extend request while the
  app was in the partial `2 active, 3 available` topology.
- Routed Extend through the hidden Windows `DisplaySwitch.exe /extend` command,
  matching the native Win + P projection behavior that re-enumerates connected
  targets. The other three projection modes retain the fast direct API path.
- Added regression coverage for partial Extend detection and for the Windows
  projection-command route.
- Incremented the Store package to `1.0.8.0`.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and read-only live checks: 83 passed, 0 failed.
- Final live display topology: Extend, 3 active displays, 3 available displays.
- Verified the Windows projection command exists at
  `C:/Windows/System32/DisplaySwitch.exe`.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.8.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `C69302708A99300D94DC80F15D2EC8F88C7A1BC3E1C07A63D2A174187C6DB3B3`.
- MSIXUPLOAD SHA-256:
  `37051E510C6E7C0244C9A9A260AE543E5DA4B94DEF8901AADD4FAAD22B8B003E`.
- Lite EXE SHA-256:
  `647660A74E42F6751C80342CC1A220A004C43030260E2E87611447DCFCAE6110`.

## 2026-08-13 - construct Extend from all connected targets

### Implemented

- Replaced the saved global Extend request with an explicit CCD topology.
- Queries every possible display path, groups paths by available target, and
  selects one distinct source for each connected target using bounded
  backtracking so all monitors are activated in Extend rather than restoring a
  prior two-display arrangement.
- Applies the selected paths with Windows best-mode logic and persists the
  resulting topology. The Windows projection command remains a compatibility
  fallback if the explicit request cannot be applied.
- Added selector tests for successful three-target assignment and an impossible
  shared-source topology.
- Added a read-only live `SDC_VALIDATE` check for the exact explicit topology.
- Incremented the Store package to `1.0.9.0`.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and read-only live checks: 87 passed, 0 failed.
- Windows accepted the constructed all-available-display Extend topology during
  non-applying validation.
- Final live display topology: Extend, 3 active displays, 3 available displays.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.9.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `528D26D18099127A55DE3AEB5C6F6E0C5A56F73C346515A60F1793199CC5B87B`.
- MSIXUPLOAD SHA-256:
  `927A1F0C28C3DCC9574741ED907267A8093631FB1811562574400875D3E1643E`.
- Lite EXE SHA-256:
  `3B09F47BE992EBD366EFC71EDE7A020A6BDBDDF6376F679421A67D1E37B3EC7D`.

## 2026-08-13 - preserve saved monitor layout during Extend

### Implemented

- Removed the explicit all-path topology introduced in `1.0.9.0` because
  Windows best-mode reconstruction could reset monitor placement, rotation,
  resolution, and orientation and persist the reconstructed arrangement.
- Extend no longer supplies display paths, invalidates mode information, or
  writes a custom topology to the CCD database.
- Added a non-destructive transition plan for the two failing routes:
  Duplicate or second screen only first changes to PC screen only, waits for
  that topology, and then invokes Windows Extend. This uses the empirically
  working PC-screen-only to Extend route while leaving saved layout restoration
  to Windows.
- Other transitions remain single-step requests.
- Incremented the Store package to `1.0.10.0`.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and read-only live checks: 87 passed, 0 failed.
- Tests cover both bridged transitions, the direct PC-screen-only to Extend
  route, and unchanged non-Extend transitions.
- No automated display-changing command was invoked during verification.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.10.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `957EF66049F64DCF37F9790D537445B97145A7EB4571621EAA55028F4DF50AFC`.
- MSIXUPLOAD SHA-256:
  `AFB0A494DBC6A49D88C516AB7DCECBD1AC2A8AF20447FC545B2795118A5B722A`.
- Lite EXE SHA-256:
  `1E0CDD30A3A58206A8B7EDC0C782F76C487B6C6788DD8363985CC3DCD7110982`.

## 2026-08-13 - restore the exact Extend layout directly

### Implemented

- Added in-memory capture of a complete active Extend configuration before the
  app changes to PC screen only, Duplicate, or second screen only.
- The snapshot retains the original CCD path and mode arrays, including monitor
  positions, target rotations, resolutions, refresh information, and path
  priority.
- Returning to Extend first reapplies the exact captured configuration with
  `SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG`. It does not request best-mode
  changes and does not save or overwrite the CCD database.
- A captured layout restores Extend in one direct step, bypassing the
  PC-screen-only bridge and Windows projection command. The bridge remains only
  when the resident app has never observed a complete Extend layout, and the
  projection command remains a fallback if exact replay is rejected.
- Incremented the Store package to `1.0.11.0`.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and read-only live checks: 92 passed, 0 failed.
- Captured the current 3-display Extend path and mode arrays and successfully
  validated the exact replay through `SDC_VALIDATE`; no display change was
  applied during verification.
- Tests cover capture, direct restore without projection, restore failure
  fallback, direct transition planning when a snapshot exists, and bridge
  planning when it does not.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.11.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `B72204CFAAAF53407C176D1D2EE89E491EC00EF02CF604822ED626D251D07AB6`.
- MSIXUPLOAD SHA-256:
  `EBD3BEF54075704C64957EE63E1602B7955190BA9EB1933094F5CB9F1462BD92`.
- Lite EXE SHA-256:
  `B23991D8A87E93297423182834119B6E430ACDC1F59D1657EB44198F4859F462`.

## 2026-08-15 - align the selected mode with Win+P

### Implemented

- Reproduced a live mismatch where path-based classification reported Extend
  with 2 active and 3 available displays while Win+P reported Second screen
  only.
- Added an authoritative read of the Windows CCD database topology through
  `QueryDisplayConfig(QDC_DATABASE_CURRENT)` and mapped its topology ID to the
  four projection modes.
- Retained `QDC_ALL_PATHS` for active and available display counts, with the
  existing path-based mode classifier as a fallback when Windows does not
  return a standard projection topology.
- Made the live exact-layout capture check safe in every projection mode. It
  now verifies capture only for a complete Extend topology and otherwise
  verifies that the saved layout is not overwritten.
- Incremented the Store package to `1.0.12.0`.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and read-only live checks: 94 passed, 0 failed.
- Final live display topology: Second screen only, 2 active displays, 3
  available displays, matching Win+P.
- Exact Extend layout capture was safely skipped in the incomplete/non-Extend
  live state; no display change was applied during verification.
- Guarded Store packaging succeeded for identity `Dreamle.WinQuickSwitch`,
  x64, version `1.0.12.0`, minimum Windows build `19041`.
- MSIX SHA-256:
  `54E8DA8F0BF6F8B4CD8DFA3453D375505E28AC7C3D2F4295CCC555FFA247BF6B`.
- MSIXUPLOAD SHA-256:
  `8D3FD46FC2D30220D4B93BBAA65A47560D69A589421E80F096A7CE495E9C0642`.
- Lite EXE SHA-256:
  `0A804610FCC9140E45C902A93B9733A211BD645358B7BED7CAC5146C0C05C781`.

## 2026-08-22 - Phase 0 edition contract

### Implemented

- Recorded the two-edition contract: the existing `WinQuickSwitch` Store
  product remains Paid, and a new `WinQuickSwitch Free` product will contain
  direct switching, favorites, controls, and keybinds without Profiles.
- Marked named profiles, profile persistence, profile hotkeys, profile tray
  actions, combined profile application, and future profile automation as
  Paid-only.
- Recorded the safety boundaries for the split: Free must not load or dispatch
  profile functionality, Paid keeps the existing local data path, and only one
  edition may own the resident process and global hotkeys at a time.
- Recorded that public standalone Lite/Portable artifacts are Free-only until a
  separate enforceable licensing path exists for Paid artifacts.
- Added the edition/build-selection architecture and Store licensing plan. No
  package files were changed in this phase.
- Tightened exact-layout capture so it requires Windows' authoritative complete
  Extend topology before retaining a layout; Second screen only, Duplicate,
  and partial Extend states cannot overwrite the saved snapshot.

### Baseline verification

- Release solution build: 0 warnings and 0 errors.
- Read-only regression and live checks: 94 passed, 0 failed.
- Live topology: Second screen only, 3 active displays, 3 available displays.
- Exact-layout capture safely skipped outside complete Extend; no display change
  was applied during verification.

## 2026-08-22 - Phase 1 central edition configuration

### Implemented

- Added the central `ProductConfiguration` model with Paid and Free editions,
  edition-specific display names and settings-directory names, and the first
  paid-only feature flag for Profiles.
- Added the validated MSBuild property `WinQuickSwitchEdition=Free|Paid`,
  defaulting to Paid for compatibility, with edition compile constants.
- Passed the central configuration into `MainWindow` without changing the
  existing Paid composition. Profile gating and UI removal remain Phase 2 and
  Phase 3 work respectively.
- Added regression coverage for the Paid and Free feature catalogs.

### Verification

- Paid Release solution build: 0 warnings and 0 errors.
- Paid regression and read-only live checks: 96 passed, 0 failed.
- Free application Release build: 0 warnings and 0 errors.
- Paid application Release build: 0 warnings and 0 errors.
- Live topology remained Second screen only, 3 active displays, 3 available
  displays; no display-changing command was applied.

## 2026-08-22 - Phase 2 paid-only Profile boundary

### Implemented

- Added `IProfileFeature` with enabled Paid and disabled Free
  implementations.
- Moved profile-store loading, profile persistence, and profile shortcut
  projection behind that boundary.
- Free composition now receives a disabled profile store and does not construct
  `JsonProfileStore`; it also returns no profile shortcut bindings.
- Profile hotkey dispatch and Profiles panel activation are guarded by the
  edition feature catalog. Paid keeps the existing profile behavior.
- Added focused tests proving Free does not load/save profile data and Paid
  loads the catalog and exposes valid profile shortcuts.

### Verification

- Paid Release solution build: 0 warnings and 0 errors.
- Paid regression and read-only live checks: 98 passed, 0 failed.
- Free application Release build: 0 warnings and 0 errors.
- Paid application Release build: 0 warnings and 0 errors.
- Complete Extend layout was captured and validated read-only; no display
  change was applied during verification.

## 2026-08-22 - Phase 3 edition-aware shell and state isolation

### Implemented

- Added edition-aware panel navigation. Free removes the Profiles tab and
  panel, keeps About in the expected keyboard position, and updates the About
  copy to describe the core-only product; Paid retains the existing six-panel
  layout and licensed copy.
- Added regression coverage for Free/Paid panel lists, Ctrl-number mapping,
  and the edition-specific `%LOCALAPPDATA%` settings paths.
- Kept Paid settings at `%LOCALAPPDATA%\\WinQuickSwitch\\settings.json` and
  moved Free settings to `%LOCALAPPDATA%\\WinQuickSwitch Free\\settings.json`.
- Made the shared sign-in startup registration safe for coexistence: disabling
  an edition only deletes the Run value when it still points to that edition's
  executable. The shared resident mutex continues to ensure only one edition
  owns global hotkeys at a time.

### Verification

- Release solution build: 0 warnings and 0 errors.
- Regression and read-only live checks: 101 passed, 0 failed.
- Free application Release build: 0 warnings and 0 errors.
- Paid application Release build: 0 warnings and 0 errors.
- `git diff --check` passed.

## 2026-08-22 - Phase 10 detachable Display widget

### Implemented

- Added a standalone, resizable `FloatingDisplayWidgetWindow` for the four
  display actions.
- Added optional pinning so the widget can remain above other windows without
  forcing every floating widget to be topmost.
- Added persisted size, position, open state, and pin state with normalized
  bounds and safe current-monitor work-area clamping.
- Added **Floating display** and **Back to main window** actions while routing
  all display changes through `MainWindow.ApplyDisplayModeAsync`.
- Added settings normalization and JSON round-trip coverage for floating
  widget layouts.
- Added a reset-layout action that restores a safe default size and position.

## 2026-08-22 - Phase 11 floating Audio and Devices surfaces

### Implemented

- Added a shared lightweight `FloatingQuickWidgetWindow` host for detached
  Audio and Devices actions.
- Added floating Audio favorites for saved playback and microphone endpoints;
  actions route through the existing favorite endpoint coordinator.
- Added floating Devices Wi-Fi and Bluetooth actions backed by the existing
  wireless-radio service and refresh gate.
- Added persisted layouts, pinning, reset, reattach, and safe work-area
  clamping for both new floating surfaces.
- Kept detailed Audio and Devices panels available through **Open details**.

### Verification

- Debug solution build: 0 warnings and 0 errors.
- Dependency-free regression suite: 99 passed, 0 failed.
- `git diff --check` passed.

### Verification

- Debug solution build: 0 warnings and 0 errors.
- Dependency-free regression suite: 99 passed, 0 failed.
- `git diff --check` passed.
- Live display topology and exact-layout capture remained read-only; no
  display-changing command was applied.

## 2026-08-22 - Phase 4 edition-aware Store packaging

### Implemented

- Extended `Build-StorePackage.ps1` with a validated `-Edition Paid|Free`
  parameter and passed the selected edition into the MSBuild publish.
- Preserved the existing Paid identity (`Dreamle.WinQuickSwitch`) and added
  explicit Free identity/publisher arguments that fail closed until Partner
  Center reserves the new product.
- Made package display names and descriptions edition-specific and isolated
  generated artifacts under `artifacts/store-msix/paid` and
  `artifacts/store-msix/free`.
- Enabled publish restore when the requested `win-x64` target is not present in
  the existing assets file, avoiding a misleading package-build failure.

### Verification

- Paid Store package build completed with the existing identity, x64 manifest,
  and `runFullTrust` declaration.
- Paid package: `artifacts/store-msix/paid/WinQuickSwitch_1.0.12.0_x64.msix`.
- Paid upload: `artifacts/store-msix/paid/WinQuickSwitch_1.0.12.0_x64.msixupload`.
- Free packaging without identity was rejected before publish, as intended.
- PowerShell syntax validation and `git diff --check` passed.

## 2026-08-22 - Phase 5 Free standalone artifacts

### Implemented

- Changed the checked-in Lite and Portable publish profiles to default to the
  Free edition, matching the public-artifact licensing boundary.
- Added edition-specific output folders: `win-x64-free-lite`,
  `win-x64-free-portable`, and opt-in `win-x64-paid-*` folders for internal
  Paid validation.
- Kept explicit `-p:WinQuickSwitchEdition=Paid` overrides available without
  changing the public Free defaults.

### Verification

- Free Lite publish completed: `artifacts/win-x64-free-lite/WinQuickSwitch.exe`.
- Free Portable publish completed:
  `artifacts/win-x64-free-portable/WinQuickSwitch.exe`.
- Explicit Paid Lite override completed in the isolated
  `artifacts/win-x64-paid-lite` folder.
- `git diff --check` passed.

## 2026-08-22 - Phase 6 cross-edition validation

### Verification

- Default Paid Release solution build: 0 warnings and 0 errors.
- Default Paid regression and read-only live checks: 101 passed, 0 failed.
- Free-property regression and read-only live checks: 101 passed, 0 failed.
- Paid staged manifest verified for identity, version `1.0.12.0`, minimum
  Windows build `19041`, and `runFullTrust`.
- Free and Paid application builds and standalone publishes completed without
  warnings or errors.
- No display-changing command was applied during validation; live display
  checks only read topology and validate the captured layout.

## 2026-08-22 - Phase 7 release checklist and handoff

### Ready locally

- Paid Store packaging remains tied to `Dreamle.WinQuickSwitch` and produces a
  verified x64 `.msix` and `.msixupload`.
- Free Lite and Portable artifacts are separated from Paid outputs and contain
  the core controls without active profile persistence, navigation, or hotkey
  dispatch.
- Documentation records the product split, settings paths, startup ownership,
  package commands, and the remaining Store checkpoints.

### External release checkpoints

- Reserve the `WinQuickSwitch Free` Partner Center product and provide its real
  identity and publisher to the Free MSIX command.
- Complete Store listing artwork, hosted EULA/privacy URLs, certification,
  signed/private-flight install testing, and the package-aware startup review.
- Keep the existing Paid listing and version history unchanged when uploading
  Paid updates.

## 2026-08-22 - Phase 8 optional sidebar overlay MVP

### Implemented

- Added backward-compatible overlay settings with safe defaults: disabled,
  35% dimming, and a 420-DIP sidebar.
- Added an append-only `ToggleOverlay` hotkey action so existing hotkey IDs do
  not change.
- Added a borderless, topmost, monitor-aware `SidebarOverlayWindow` with a
  dimmed backdrop, right-side display actions, Esc handling, close handling,
  and backdrop-click dismissal.
- Added an Options switch that routes the existing show/hide shortcut to the
  overlay when enabled and preserves the compact resident window when disabled.
- Documented the overlay behavior and exclusive-fullscreen limitation.

### Verification

- Release-independent Debug solution build: 0 warnings and 0 errors.
- Dependency-free regression suite: 97 passed, 0 failed.
- No display-changing command was applied during implementation or validation.
- Manual visual validation of the WPF overlay remains pending on a Windows
  desktop with multiple monitors and mixed DPI.

## 2026-08-22 - Phase 9 compact home surface

### Implemented

- Made the existing Display panel action-first with a Quick switch heading and
  the four projection actions remaining immediately available.
- Added an at-a-glance summary for the current display mode, audio inventory or
  favorites, and connected-device inventory state.
- Added direct actions for the sidebar overlay, detailed Audio controls, and
  detailed Devices controls without changing the existing panel navigation or
  keyboard IDs.
- Added a shortcut hint that reflects the user-configured show/hide shortcut.
- Made display switching execute on a dedicated background worker so native
  display calls cannot run on the UI thread.

### Verification

- Debug solution build: 0 warnings and 0 errors.
- Dependency-free regression suite: 97 passed, 0 failed.
- `git diff --check` passed.

## 2026-08-22 - overlay recovery, tray actions, and main-window pinning

### Implemented

- Made tray **Open main window** dismiss any visible sidebar overlay before
  revealing the normal window, preventing the topmost dimmed layer from hiding
  the app after an overlay session.
- Added stale-overlay cleanup when the overlay has already closed, so a later
  open request cannot target a detached window instance.
- Kept explicit **Open main window** and **Exit** actions in the native tray
  menu, with clearer wording for the recovery action.
- Added a discoverable **Pin** header toggle and a matching Options checkbox.
  Pinning persists, keeps the normal window visible when focus changes, and
  makes it topmost; clearing it restores the resident click-away behavior.
- Preserved the default unpinned state for older settings files.

### Verification

- Debug solution build completed with 0 warnings and 0 errors; the
  dependency-free regression suite passed 99/99 tests. No display topology
  command is used by this behavior.

## 2026-08-22 - overlay-to-main navigation and reliable tab clicks

### Implemented

- Added an **Open main window** button to the display-only overlay so users can
  reach Audio, Devices, Options, Profiles, and About without using the tray.
- Changed the main-window tabs from `Checked`-only navigation to an explicit
  click handler. Repeated clicks and clicks on the current tab now route through
  the same panel activation path reliably.
- Made panel-name parsing case-insensitive while preserving edition gating and
  existing keyboard navigation.

### Verification

- Debug solution build completed with 0 warnings and 0 errors.
- Dependency-free regression suite passed 99/99 tests.
- `git diff --check` passed.

## 2026-08-22 - deferred tray exit teardown

### Implemented

- Deferred the tray **Exit WinQuickSwitch** shutdown request until the native
  context menu has returned from `TrackPopupMenu`. This keeps the owner window
  and tray menu alive long enough for the command to complete, then follows the
  normal `_isExiting` cleanup path.

### Verification

- Debug solution build completed with 0 warnings and 0 errors.
- Dependency-free regression suite passed 99/99 tests.
- `git diff --check` passed.

## 2026-08-22 - overlay feature hub

### Implemented

- Replaced the display-only sidebar content with a compact feature hub for
  Display, Audio, Devices, Options, Profiles, and About.
- Kept Profiles edition-aware: the tab is hidden in Free builds and shows
  saved-profile apply actions in Paid builds.
- Added overlay actions for display modes, audio and microphone favorites,
  volume/sound settings, Wi-Fi and Bluetooth controls, device settings,
  theme/overlay/pin/startup/taskbar toggles, Windows settings links, profiles,
  and legal documents.
- Routed every action through the existing MainWindow coordinator and platform
  services. The overlay does not create duplicate display, audio, or device
  watchers, and full-panel handoffs close the topmost overlay first.
- Added one-shot refresh/handoff behavior for Display, Audio, Devices, and
  Options so opening a tab while the resident window is hidden still shows
  current state.

### Verification

- Debug solution build completed with 0 warnings and 0 errors.
- Dependency-free regression suite passed 99/99 tests.
- `git diff --check` passed.

## 2026-08-23 - feature-complete workspace foundation

### Implemented

- Added the edition-aware overlay feature catalog, typed quick-switch command
  records/results, refresh scopes, and smart-close metadata. The catalog is
  now covered by parity tests so a feature cannot silently exist in only one
  surface.
- Added `OverlayWorkspaceWindow` as the active overlay shell: Home, fixed
  right launcher, essential cards, drag/resize/snap, z-order, hide/restore,
  feedback, expandable inspector, monitor-aware placement, keyboard navigation,
  `Ctrl+0`, edition-aware `Ctrl+1`–`Ctrl+6`, Escape, backdrop close, Open main
  window, and Exit.
- Added versioned `OverlayWorkspaceSettings` with per-monitor layouts keyed by
  `MONITORINFOEX.szDevice`, inspector-width persistence, legacy sidebar-width
  migration, adaptive defaults, edge clamping, and Free/Paid Profiles gating.
- Routed workspace actions through the existing display, audio, device,
  taskbar, profile, legal, floating-widget, and settings services. The
  workspace does not create duplicate watchers or display mutation paths, and
  native Settings handoffs dismiss the topmost workspace first.
- Added endpoint and connected-device inventories to the Audio/Favorites and
  Devices inspectors, including direct default/calls selection and radio
  refresh actions. Free settings normalization now strips any persisted Paid
  Profiles layout before it can be restored.
- Kept overlay layout changes presentation-only; the existing authoritative
  display transition, topology settlement, placement, and rotation safeguards
  remain unchanged.

### Verification

- Debug solution build completed with 0 warnings and 0 errors.
- Free-edition Debug build completed with 0 warnings and 0 errors.
- Dependency-free regression and parity suite passed 107/107 tests.
- Remaining M8.2 work is explicit: extract the coordinator/state snapshots and
  host the full reusable feature views in the inspector before release parity
  validation.

## 2026-08-23 - v1.0.13.0 overlay recovery and release artifacts

### Implemented

- Kept the workspace opening when a Windows session denies `GetCursorPos` by
  falling back to the owner window's monitor and then the virtual screen.
- Guarded workspace construction, layout restoration, and `Show()` as one
  recovery path. A failed overlay now reports the reason and restores the
  normal window instead of leaving the app hidden.
- Stamped the executable, app manifest, Store manifest, tray tooltip, About
  surface, and Lite/Portable output folders with release version `1.0.13.0`.
- Versioned Store output folders as `artifacts/store-msix/<edition>/v<version>`
  so upload links cannot silently point at an older package.

### Verification

- Paid and Free Debug builds completed with 0 warnings and 0 errors.
- Dependency-free regression and parity suite passed 107/107 tests.

## 2026-08-23 - v1.0.14.0 per-monitor overlay and tray recovery

### Implemented

- Corrected the `MONITORINFOEXW` interop layout to Unicode. The previous ANSI
  structure size caused monitor queries to fail even when Windows returned a
  valid pointer monitor.
- Removed the virtual-screen fallback that could stretch the dimmed workspace
  across every display. Fallback placement now always resolves one monitor.
- Decoded `NOTIFYICON_VERSION_4` callbacks from the low word of `lParam` and
  handled both right-button and context-menu notifications.
- Used the version-4 notification anchor when a pointer query is unavailable,
  keeping the tray menu reachable on negative-coordinate and protected-screen
  scenarios.
- Advanced executable, standalone, and Store artifacts to version `1.0.14.0`.

### Verification

- Paid and Free Debug builds completed with 0 warnings and 0 errors.
- Regression and parity suite passed 109/109 tests, including new monitor
  structure-size and tray callback-decoding checks.
- Paid Store MSIX and MSIX upload packaging completed successfully.
- Automated attended UI control was unavailable in this desktop session, so
  final multi-monitor placement and tray clicking remain a user-attended
  release check.

## 2026-08-23 - v1.0.15.0 collision-free workspace layout

### Implemented

- Preserved widget coordinates until WPF reports a real canvas size. This
  prevents pre-show initialization from collapsing every default or persisted
  card to the same top-left position.
- Replaced the fragile `(24,24)` default-layout sentinel with measured-layout
  initialization and deterministic adaptive tiling for the four default
  widgets.
- Added snapped collision detection and first-free-slot placement. A hidden
  widget restored from the launcher opens in unused space while existing
  custom positions stay unchanged.
- Repaired overlapping legacy placements one card at a time, and reflowed all
  visible cards only when the active monitor cannot accept the new card at its
  current size.
- Added a visible 28-DIP diagonal resize grip outside the footer controls and
  a visible inspector resize handle. Widget clicks now also allocate a unique
  frontmost z-index.
- Advanced executable, standalone, and Store artifacts to version `1.0.15.0`.

### Verification

- Paid Debug build completed with 0 warnings and 0 errors.
- Free Debug build completed with 0 warnings and 0 errors.
- Regression and parity suite passed 111/111 tests, including default-card
  separation and first-free-slot placement.
- Paid/Free Lite and Portable executables were published with file version
  `1.0.15.0`; the Paid Store MSIX and MSIX upload package completed
  successfully.
- `git diff --check` passed. Repository-wide `dotnet format` still reports
  pre-existing mixed line endings in unrelated audio and widget files, so no
  bulk line-ending rewrite was performed.

## 2026-08-23 - v1.0.16.0 persistent quick widgets and complete launcher panels

### Implemented

- Persisted the final measured card layout after initialization and retained
  custom Left, Top, Width, Height, visibility, and z-order across workspace
  close/reopen cycles.
- Added compatibility matching between legacy `DISPLAY1` and
  `MONITORINFOEXW` `\\.\DISPLAY1` monitor identities. A legacy
  `virtual-screen` layout can also migrate to the current single-monitor key.
- Wired feature-specific default dimensions from `FeatureDescriptorCatalog`
  into card composition. Adaptive tiling shrinks those presets only when the
  current monitor needs it, while compact actions wrap to use available card
  space.
- Added top-right **Reset size** and **Remove** controls to every quick widget.
  Removed cards remain in persistence and can be restored with **Add quick
  widget** from the complete right-side feature panel.
- Changed the fixed right navigation into the primary complete surface. It now
  hosts the existing full Display, Audio/Favorites, Devices, Options,
  Paid Profiles, and About panels directly inside the overlay inspector; quick
  widgets contain only common actions.
- Restored temporarily hosted feature panels to their original main-window
  visual parent on inspector close, workspace close, main-window handoff, and
  direct window closure.
- Advanced executable, standalone, and Store artifacts to version `1.0.16.0`.

### Verification

- Paid Debug build completed with 0 warnings and 0 errors.
- Free Debug build completed with 0 warnings and 0 errors.
- Regression and parity suite passed 112/112 tests, including custom bounds,
  removed-state persistence, and legacy/current monitor identity matching.
- Paid and Free Lite/Portable executables were published with file version
  `1.0.16.0`; the Paid Store MSIX and MSIX upload package completed
  successfully.

## 2026-08-23 - v1.0.17.0 content-sized workspace cards

### Implemented

- Enlarged every feature-specific card preset so the quick workspace shows
  substantially more content by default. Untouched v1.0.16.0 preset sizes
  migrate automatically; genuinely custom user dimensions remain unchanged.
- Removed the redundant Home entry from the right launcher while retaining
  `Ctrl+0` as the quick-workspace return shortcut.
- Suppressed routine Loading/Ready feedback when changing launcher features.
  The bottom feedback surface remains available for mutations, warnings, and
  failures.
- Replaced the Reset size and Remove header labels with compact, accessible
  icon controls that retain complete tooltips and automation names.
- Added the connected-device inventory to the Devices quick card. Device
  refreshes now wait for an active refresh instead of silently returning, and
  successful refreshes rebuild the visible card with current data.
- Advanced executable, standalone, and Store artifacts to version `1.0.17.0`.

### Verification

- Paid and Free Debug builds completed with 0 warnings and 0 errors.
- Regression and parity suite passed 114/114 tests, including launcher Home
  removal and safe migration from the old card presets.
- Paid and Free Lite/Portable executables were published with file and product
  version `1.0.17.0`; the Paid Store MSIX and MSIX upload package completed
  successfully with manifest version `1.0.17.0`.
- `git diff --check` passed with only the repository's existing line-ending
  notice for `WidgetSettings.cs`.

## 2026-08-23 - v1.0.19.0 stable widget reopen and refresh layout

### Implemented

- Made the feature catalog the single source for every non-custom widget
  preset. Refreshing a card now updates its recommended size while preserving
  its saved position, visibility, and z-order.
- Reused the measured grid-cell limits when applying those presets, so a
  narrow monitor gets the same effective size after refresh, reopen, and an
  explicit reset.
- Limited collision repair and default tiling to the initial measured restore
  (or an explicit reset). Audio/device refreshes and later size changes no
  longer re-tile user-arranged cards.
- Removed the stale `virtual-screen` fallback for unknown monitors. A monitor
  without a saved physical layout receives a fresh default composition and
  saves it under that monitor identity after measurement.
- Deferred the first layout save until the workspace is measured, preventing a
  transient WPF size event from overwriting a restored position.
- Added a regression test for physical-monitor lookup and advanced all release
  metadata to version `1.0.19.0`.

### Verification

- Paid/Free regression and parity suite passed 116/116 tests.
- The new build is published with versioned executable, MSIX, and MSIX upload
  paths so the reopen/refresh fix can be tested independently of v1.0.18.0.

## 2026-08-23 - v1.0.18.0 durable content-based widget layout

### Implemented

- Added layout schema version 2 with an explicit `HasCustomSize` state. Preset
  widgets now reapply their feature-specific recommendation on reopen, while a
  user-resized widget retains its exact saved width and height.
- Migrated the old generated equal-size grid once to the four intended default
  quick widgets: Display, Audio, Devices, and Favorites. Profiles, About,
  Options, and Home remain hidden until explicitly added.
- Retuned presets around each compact surface: Display is reduced to the space
  needed for four projection controls, Audio/Favorites allow wrapped endpoint
  actions, and Devices retains the larger scrollable inventory area.
- Repaired widget movement by tagging each drag handle with its stable widget
  ID. The previous implementation depended on a visual-parent hierarchy that
  changed when the header action icons were introduced.
- Preserved custom sizes during collision repair and adaptive reflow instead
  of replacing them with feature presets.
- Advanced executable, standalone, and Store artifacts to version `1.0.18.0`.

### Verification

- Paid and Free Debug builds completed with 0 warnings and 0 errors.
- Paid and Free regression/parity runs passed 115/115 tests, including schema-v1 grid
  migration, default visibility, content-based presets, and custom-size JSON
  persistence.
- Paid and Free Lite/Portable executables were published with file and product
  version `1.0.18.0`; the Paid Store MSIX and upload package were created with
  manifest version `1.0.18.0`.
- `git diff --check` passed with only the repository's existing line-ending
  notice for `WidgetSettings.cs`.
## 2026-08-26 - Default-size resize floors (v1.0.32.0)

- Resizable workspace widgets now use their full default preset as their minimum expanded size: Audio 760×600, Devices 840×820, and Paid Profiles 720×320 DIP.
- Monitor-safe clamping still wins when the active workspace is physically smaller than a preset, so small and high-DPI monitors remain usable.
- Updated geometry regression coverage to enforce the shared default-equals-minimum policy.

## 2026-08-25 - Smooth workspace geometry and widget chrome (v1.0.31.0)

- Workspace cards now track the pointer continuously instead of rounding every move to a 12-DIP grid; grid spacing remains only in the organized first-run layout.
- Bottom-right resizing now caps width and height at the canvas edge while preserving the card's left and top edges.
- Devices has a persistent minimize/restore control and a 680×460 DIP minimum expanded size.
- Display, Audio, and Devices refresh actions now live in the card header. The duplicate inner feature title and refresh button are suppressed only for workspace widgets; main-window, sidebar, and floating instances retain their complete headers.
- Added geometry regression coverage for edge-limited resizing and the Devices minimum. Release build and tests pass with zero warnings/errors and 126/126 tests.

## 2026-08-25 - Audio volume drag stability (v1.0.30.0)

- App-volume sliders now edit locally during a drag and commit once on release or keyboard adjustment, instead of issuing a native command for every pointer tick.
- Shared audio rows are reused by session ID during refreshes, preserving mouse capture and the active slider while inventory state updates.
- Successful shared volume and mute commands refresh the audio inventory before publishing the next snapshot, and the shared view now displays the volume percentage.
- Added a regression test for live row identity, stale-refresh protection, and one-shot volume commit. Build and tests pass with zero warnings/errors and 125/125 tests.

## 2026-08-26 - Responsive widget chrome and overlay-first startup (v1.0.33.0)

- Initial process launch now honors the saved overlay preference. The resident
  main-window handle is created first so tray and global
  shortcut support remain available even when no normal window is shown.
- The overlay's explicit Open main window action, tray Open, and later process
  activations continue to open the normal app window rather than toggling or
  dismissing the configured workspace unexpectedly.
- Workspace visuals can shrink below feature presets only when monitor-safe
  clamping requires it. This prevents Audio and Devices from rendering beyond
  the right edge of a narrow active monitor while retaining the normal preset
  resize floors on larger monitors.
- Resizable cards reserve the lower-right resize corner so Devices actions and
  list content no longer collide with the grip.
- Audio tools now wrap beneath the status line instead of competing with the
  workspace card controls, and the shared Audio/Devices views no longer impose
  a second visual minimum width.
- Options now keeps General shortcuts in an accessible, collapsed-by-default
  show/hide section. Favorite shortcut groups remain separately collapsible.

### Verification

- Paid and Free Release builds completed with zero warnings and zero errors.
- The complete automated regression/parity suite passed 126/126 tests.
- An isolated normal Windows launch with the saved overlay preference produced
  a resident process without a normal main-window handle, confirming that the
  first-launch path selected the overlay surface. The borderless overlay itself
  is not targetable by the Windows capture helper, so final pixel-level review
  remains an attended check.
- Paid/Free Lite and Portable executables plus the Paid Store MSIX and upload
  package were produced under versioned v1.0.33.0 paths.

## 2026-08-28 - Isolated Windows Sandbox UI validation

- Added a host launcher that publishes the selected Paid or Free working tree
  into a timestamped test payload and generates a locked-down .wsb
  configuration.
- Added an in-Sandbox Windows UI Automation runner for the conventional window
  and full overlay. It captures every edition-supported panel in dark and
  light themes, including a live theme change while each interface is open.
- Added edition-aware navigation checks, verification that the removed
  "Quick workspace" heading remains absent, structured JSON results, execution
  logs, and a host-side HTML screenshot gallery.
- The Sandbox receives a read-only application payload and one writable result
  folder. Networking, clipboard, microphone, camera, printer, and virtual GPU
  access are disabled.
- The suite deliberately never invokes projection, audio endpoint, radio,
  taskbar, startup, profile-apply, Windows Settings, or other hardware-changing
  actions. Real mixed-DPI monitors and device hot-plugging remain attended
  physical checks.

## 2026-08-28 - Deterministic full-feature Sandbox E2E suite

- Added a test-only application composition enabled by
  `WinQuickSwitchE2E=true` and `--sandbox-e2e`. Production builds exclude all
  simulated service code.
- Seeded safe display, audio endpoint/session, device, wireless-radio, taskbar,
  startup, and Paid profile fixtures while retaining the real WPF windows,
  shared feature controls, coordinator, persistence, overlay, widgets, and
  theme resources.
- Added a machine-readable feature manifest covering the conventional window,
  overlay launcher/inspectors, floating Display/Audio/Devices windows, display
  modes, audio roles/favorites/volume/mute, radios, Settings routes, taskbar,
  startup, Paid profiles, legal content, and edition boundaries.
- Expanded the guest UI Automation runner to execute safe simulated mutations
  and validate their JSONL service journal. Free runs fail if any profile
  service action appears.
- The remaining attended boundary is limited to behavior Sandbox cannot model:
  physical monitor/DPI topology, real endpoint routing, Bluetooth/USB hot-plug,
  Store package installation, and shell timing on supported host builds.

## 2026-08-28 - v1.1.0 shared refresh ordering

- Added a reusable latest-refresh gate for shared coordinator state. Starting a
  newer Audio or Devices refresh cancels the superseded request without letting
  the older lease clear or cancel the newer one.
- Routed shared Audio and Devices refreshes through cancellation tokens that are
  linked to the requesting surface, visible-window lifetime, and application
  lifetime.
- Changed coordinator-triggered Audio and wireless refreshes to wait for the
  active platform read after cancelling it, avoiding the previous gap where a
  newer request could return without obtaining a fresh snapshot.
- Added cancellation checks immediately after platform inventory calls and
  before WPF controls or immutable shared state are updated.
- Corrected Audio and Devices completion ordering so the coordinator publishes
  the final Ready snapshot only after the legacy refresh state is cleared.
  Shared radio and endpoint commands no longer remain disabled after a
  successful inventory refresh.
- Restored the last known radio presentation if a superseded wireless read is
  cancelled, preventing hidden legacy controls from remaining disabled when a
  surface is reopened.
- Kept ordinary watcher refreshes debounced and non-blocking. The ordering gate
  applies only to coordinator requests from shared surfaces.
- Release build completed with zero warnings and zero errors. The regression,
  parity, persistence, and platform-boundary suite passed 130/130 tests.
- The static Windows Sandbox harness passed. Hidden end-to-end runs passed
  41/41 Paid checks with 26 screenshots and 35/35 Free checks with 23
  screenshots. Both runs ended with zero remaining Sandbox processes or
  visible windows.
- Added foreground-window capture and restoration for ordinary overlay
  dismissal, global overlay-toggle dismissal, and successful smart-close
  actions. Programmatic handoffs to Settings, the main window, legal content,
  or floating widgets do not steal focus back from their destination.
- Inspector navigation now moves keyboard focus into the first usable feature
  control, while Ctrl+0 (including Ctrl+Numpad0) returns to the workspace.
  Non-actionable drag surfaces no longer appear in the Tab order.
- Increased inspector and widget resize hit targets to 40 DIPs and made overlay
  feedback a named polite live region for screen readers.
- Guarded global/profile hotkeys, tray favorites, and overlay initialization so
  asynchronous failures remain visible without escaping an async event entry
  point. Auxiliary overlay and floating windows are now closed even if a
  partial startup left them hidden.
- Expanded Sandbox keyboard checks passed 43/43 for Paid and 37/37 for Free.
  The new checks confirmed focus moved to `Refresh display status`, Ctrl+0
  returned to the workspace, and teardown again left zero processes and zero
  visible Sandbox windows.
- Repository-wide formatting verification still reports pre-existing mixed
  line endings and whitespace in unrelated dirty files; this slice does not
  rewrite those user-owned changes.
- The product version remains 1.0.33.0 during v1.1.0 development and will move
  to 1.1.0.0 once, when the full stabilization acceptance gates are satisfied.

## 2026-08-30 - v1.1.0 overlay accessibility and recovery gate

- Removed the retired `SidebarOverlayWindow`, its string action builders, and
  its no-op lifecycle calls. The legacy sidebar-width JSON field remains only
  as an inspector-width migration input.
- Replaced the hard-coded overlay backdrop with a dynamic theme resource and
  added light, dark, and high-contrast-safe focus/selection resources for
  sliders, checkboxes, list rows, endpoint badges, and checked toggles.
- Added contextual automation names for favorite and profile actions, device
  row descriptions, and endpoint roles. Common interactive targets and the
  overlay inspector/feedback controls now meet the 40-DIP minimum.
- Fixed launcher and card focus so keyboard navigation targets the first usable
  descendant instead of a deliberately non-focusable card container. Clicking
  or dragging a card also brings it to the front and retains its z-order.
- Added a guarded shared refresh boundary with an inline Retry state. Attach
  and direct Display, Audio, or Devices refresh failures no longer escape a
  fire-and-forget WPF path.
- Declared PerMonitorV2 DPI awareness and replaced the overlay's maximized
  monitor-lookup fallback with explicit one-monitor bounds.
- Removed the remaining launcher heading/subtitle. Generated first-run cards
  perform their final content fit before the initial grid is finalized;
  restored or user-adjusted geometry is never reflowed.
- Updated the Sandbox selectors for contextual favorite/profile automation
  names. The complete regression suite passed 133/133 tests. Paid and Free
  Release builds completed with zero warnings and zero errors.
- Final hidden Sandbox validation passed 43/43 Paid checks with 26 screenshots
  and 37/37 Free checks with 23 screenshots. Visual review confirmed readable
  light-theme selected text and monitor-safe scrolling for constrained cards.
- The product version remains 1.0.33.0 until the remaining attended
  screen-reader, physical mixed-DPI/monitor, hot-plug, tray, and Store-install
  checks complete; v1.1.0.0 will be applied once at release acceptance.

## 2026-08-30 - Isolated E2E speed and memory harness

- Added an opt-in Windows Sandbox performance mode without changing the public
  product version. Paid and Free use the same deterministic E2E composition as
  functional testing but run in fresh processes and artifact directories.
- Added cold normal-window and overlay startup timings, repeated feature and
  inspector navigation, and journal-confirmed Display, Audio, and Devices
  refresh timings. Warm-up runs are retained but excluded from median and P95.
- Added a 100-millisecond process sampler for private memory, working set, peak
  working set, handles, threads, and CPU time. Initial, final, peak, and delta
  summaries make retained-resource regressions visible across the workload.
- Kept diagnostic screenshots outside timed stages and added before/after
  captures for visual correlation without contaminating latency results.
- Added `performance.json`, optional budget enforcement, a Paid/Free comparison
  runner, HTML report, and speed/memory CSV exports. Provisional budgets report
  warnings by default and become gating only with an explicit switch.
- Expanded the static Sandbox harness to enforce the performance scenario,
  sampling, warm-up, iteration, output, and screenshot-exclusion contracts.
- Completed the first full baseline with two warm-up and twenty measured
  iterations per edition. Paid passed 21 stage groups and 482 measured stage
  samples with 3,566 process samples; Free passed 19 stage groups and 422
  measured stage samples with 2,837 process samples.
- Paid startup measured 1,488 ms for the main window and 1,956 ms for the
  overlay. Free measured 1,484 ms and 1,963 ms respectively. Overlay startup
  was the slowest P95 stage in both editions and remained below its provisional
  2,500-ms budget.
- Post-warm-up private-memory deltas were -7.15 MB main / +9.78 MB overlay for
  Paid and -2.41 MB main / +6.30 MB overlay for Free. Both editions passed all
  latency, memory, handle, and thread budgets and left zero Sandbox processes
  and zero visible Sandbox windows after cleanup.

## 2026-08-31 - Feature-complete E2E speed and memory coverage

- Expanded performance mode from startup/navigation/refresh checks to every
  safe deterministic feature action. The matrix now covers Display modes and
  routes, Audio roles/favorites/app-session controls, radio and device routes,
  floating-window lifecycle, Options state changes, legal dialogs, and Paid
  profile edit/apply/undo/copy flows.
- Added edition-aware `performanceStages` to the executable coverage manifest.
  Missing Paid/Free stages now fail a performance run independently of whether
  provisional latency/resource budgets are enforced.
- Memory evidence continues at 100-millisecond resolution through the complete
  action workload and now surfaces sample counts, peak working set, private
  memory retention, handle/thread deltas, and CPU time in the combined report.
- Added `coverage-summary.csv` and a coverage table to the Paid/Free HTML
  comparison. Hardware and Windows-setting actions remain simulated so the
  host display, audio, radios, taskbar, and startup configuration are untouched.
- Final warmed integration evidence passed both editions with all performance
  budgets enforced. Paid observed 77 stage groups with 76/76 required stages
  across 88 measured operations and 1,130 process samples; Free observed 70
  stage groups with 70/70 required stages across 79 measured operations and 865
  process samples. Post-warm-up private-memory deltas were 8.53 MB main / -3.78
  MB overlay for Paid and 3.20 MB main / 8.44 MB overlay for Free. The combined
  v1.0.33.0 report includes speed, memory, and coverage tables, with zero budget
  failures and no missing edition-required stages.

## 2026-09-01 - v1.1.0 edition-aware keyboard and responsive E2E gate

- Expanded the functional Sandbox suite with conventional-window and overlay
  Ctrl+number navigation for every edition-supported feature, launcher arrow
  navigation, Escape inspector dismissal, and a 920x700 constrained workspace
  screenshot.
- Fixed the Free overlay's numbered navigation to use the shared feature
  catalog. With Paid-only Profiles omitted, Ctrl+5 now opens About and Ctrl+6
  remains unassigned, matching the conventional window.
- Added regression coverage for Paid and Free control-number mappings and kept
  the v1.1.0 development version at 1.0.33.0 until the attended release gates
  are complete.
- The complete contract and regression suite passed 146/146 tests. Production
  Paid and Free Release builds each completed with zero warnings and zero
  errors.
- Fresh hidden Sandbox runs passed 47/47 Paid checks with 27 screenshots and
  41/41 Free checks with 24 screenshots. Both lifecycle reports recorded zero
  remaining Sandbox processes and zero remaining visible windows.
- Automated evidence now covers live dark/light theme switching, first-start
  overlay preference, edition-aware keyboard navigation, inspector focus and
  dismissal, startup/reopen behavior, and constrained responsive layout.
  Physical 100/150/200% DPI, mixed monitors, Windows High Contrast,
  screen-reader, hot-plug, notification-area Open/Exit, and Store package
  install/update remain attended release checks.

## 2026-09-02 - v1.1.0 High Contrast and accessible-name gate

- Added an edition-aware UI Automation audit for every visible keyboard control
  in the conventional feature panels and overlay inspectors. Missing accessible
  names now fail the functional Sandbox suite.
- Added test-build-only High Contrast palette captures for main-window Options
  and the constrained overlay. Production builds remain driven exclusively by
  `SystemParameters.HighContrast`; the deterministic marker is compiled only
  into the isolated E2E executable.
- Added rendered-pixel checks for checked Application labels. This caught a
  defect that semantic UI Automation could not: the controls retained names
  while their text disappeared against the white High Contrast background.
- Changed the shared checkbox template to render string content through a
  palette-bound text element, preserving checked labels during live theme
  changes across main, overlay, profile, audio, and floating surfaces.
- Fresh rebuilt Sandbox runs passed 67/67 Paid checks with 29 screenshots and
  59/59 Free checks with 26 screenshots. Each run passed four rendered label
  contrast checks and recorded zero remaining Sandbox processes and visible
  windows.
- The complete regression suite passed 146/146 tests. Paid and Free production
  Release builds both completed with zero warnings and zero errors. The public
  version remains 1.0.33.0 until the remaining attended v1.1.0 gates pass.

## 2026-09-02 - Full-screen display override recovery

- Reproduced the reported failure deterministically without mutating the host
  displays: the display API accepted Extend while every authoritative topology
  read remained second screen only. The previous main-window path incorrectly
  returned success and allowed overlay smart-close to dismiss the controls.
- Added a shared display-transition executor that verifies preparatory and final
  topologies, requires a stable requested state, retries one external override,
  and fails with the actual remaining topology when the override persists.
- Kept exact captured-Extend restoration and all placement, rotation, and
  display-path safeguards unchanged. Tests use fake display services only.
- Added regressions for persistent external override, successful bounded
  recovery, and an unsettled PC-screen-only bridge. The complete suite passed
  149/149 tests; the read-only Windows integration run passed 156/156 and
  observed the host's current Extend topology without applying any change.
- A real Valorant/Vanguard reproduction was not possible in the isolated test
  environment because it cannot provide the anti-cheat driver, reboot, and
  physical exclusive-full-screen monitor behavior. The production failure mode
  is covered by the deterministic accepted-then-overridden topology sequence.

## 2026-09-04 - Stable E2E speed and memory leak boundaries

- Fixed an Audio performance-runner race where the fake service journal could
  confirm a volume mutation before the refreshed shared Audio row had become
  enabled again. The runner now waits for both the volume and mute controls
  outside the timed stage, so control readiness no longer changes latency.
- Added a 1.4-second settling delay before boundary readings. Review rejected
  median-filtering those readings because it could conceal a late increase;
  retained-resource budgets use the latest actual reading.
- Tagged every 100-millisecond process sample with its measured iteration and
  warm-up state. Diagnostic trends compare disjoint early and late measured
  windows; the original stable boundary budgets remain enforced.
- Bumped the performance report schema to version 2 and added trend columns to
  the JSON, HTML, and memory CSV outputs. The static Sandbox harness enforces
  the new readiness, boundary, iteration, and trend contracts.
- The six-iteration qualification passed feature coverage and the provisional
  trend-only gate. Subsequent review rejected that gate: Free retained 12
  handles, exceeding the original 10-handle limit. It is not release-pass
  evidence under the restored budget rules.
- Added 20 executable analysis checks, passing under both Windows PowerShell
  5.1 and PowerShell 7. Tests cover exact limits, retained growth despite flat
  trends, warm-up exclusion, disjoint short-run windows, and missing stages.
- Missing iteration samples fail coverage; unavailable trends are null, and
  sampler iteration/warm-up context is updated atomically.
- Fixed empty-stage report generation so interrupted or failed workloads can
  report missing coverage instead of throwing a strict-mode property error.
- The latest completed 20-iteration comparison still fails resource budgets:
  Paid main +13 handles; Free main +13 handles and +15.31 MiB private memory.
  A subsequent 20-iteration run was interrupted during Paid and produced no
  completed report. No new version or release artifacts were produced.
- Re-ran the isolated application regression suite: 149/149 passed. Detailed
  evidence and remaining work are in `PERFORMANCE_CLOSEOUT_2026-09-04.md`.

## 2026-09-05 - v1.1.0.0 candidate preparation

- User confirmed real-hardware acceptance is not yet tested and requested
  completing the features before handing over one build for attended checks.
  Added `RELEASE_ACCEPTANCE_1_1_0.md`; Store publication remains separate.
- Added per-cycle resource diagnostics and client-only UI Automation proxy
  cleanup outside measured actions. No application GC was forced; resource
  limits remain unchanged. Full 20-iteration Paid/Free runs passed all resource
  limits. Paid passed overall; Free missed only overlay startup (2527.466 ms
  against 2500 ms). Historical results are preserved in the closeout report.
- Screenshot review found a themed ComboBox missing `ItemTemplateSelector`.
  Selected profile favorites now render their friendly names, verified by an
  offscreen rendered-text regression. Initial profile status is neutral, and
  the PINNED badge no longer stretches across the row.
- Compact launcher footer actions now use icons with retained accessible
  names/tooltips. Compact padding leaves room for full hit targets, and the
  keyboard hint hides only in compact mode. Tests cover narrow/wide changes.
- Hidden widgets defer shared-view construction until first measurement.
  Visible widgets still measure their complete real content before fitting,
  and later measures/visibility changes reuse the same instance. Single-panel
  navigation no longer constructs every other widget's feature view.
- Application regression suite passed 149/149 with added visual/deferred-view
  assertions. Harness and all 20 performance-analysis fixtures passed.
- Added `Test-ReleaseArtifacts.ps1`: 39 checks passed against existing .33
  artifacts; a synthetic wrong manifest version failed as intended. Checks
  cover both identities/names, executable versions, upload hashes, and absence
  of simulated E2E services without installing or launching packages.
- Store builder now reads its omitted version from `Directory.Build.props`.
  Explicit overrides, component limits, and revision-zero rules remain.
  Store instructions use the existing Free reservation, including exact spaces.
- Set the candidate version once to v1.1.0.0 in central, native, and Store
  manifests. Fresh candidate UI/performance runs and package verification must
  finish before binary handoff. No Store upload or hardware sign-off claimed.

## 2026-09-06 - First-run fit and screenshot validation

- Screenshot review caught equal-cell arrangement shrinking Devices below its
  minimum, then normalization enlarging/clamping it over Display. Generated
  layouts now pack their actual fitted/minimum sizes without shrinking or
  changing z-order. Restored/custom positions are not repacked; hidden cards
  retain their bounds. A tiny canvas that cannot fit every minimum-sized card
  uses a bounded cascade instead of violating minimum sizes.
- Complete-content measurement includes the body scroll container's margins,
  padding and border, including reserved resize-corner space. Audio's final
  action is no longer clipped after Fit content.
- Added regressions using actual 720/760/840-DIP card widths and minimum
  heights, plus an offscreen fit test for scroll-container chrome.
- Final application suite: 150/150. Read-only integration: 157/157. Paid and
  Free production Release builds: zero warnings and errors.
- Paid UI `20260906-000851-v1.1.0.0`: 68/68, 29 screenshots. Free UI
  `20260906-001431-v1.1.0.0`: 59/59, 26 screenshots. Visually verified the
  non-overlapping wide default layout, complete Audio actions, and readable
  compact launcher footer controls. These are simulated, not hardware tests.
- Production publishing profiles explicitly disable E2E service configuration.
  Final candidate performance validation and package handoff remain pending.

## 2026-09-06 - Restore widget refresh parity

- Final UI review found that Display, Audio and Devices hide their shared-view
  Refresh control in widget mode without supplying a header replacement.
  Added accessible 40-DIP header Refresh icons using the existing typed refresh
  catalog. Refresh does not invoke fitting or replace a card.
- Replaced the compact launcher Exit power symbol with the supported close
  glyph; its accessible name and tooltip remain explicit.
- WPF regression checks exercise all three header commands and unchanged
  geometry/control identity. Added sandbox checks that each header refresh
  reaches its simulated shared service. Application and read-only integration
  suites still pass 150/150 and 157/157; Free builds with zero warnings/errors;
  harness and 20/20 performance fixtures pass.
- The comparison started at `20260906-161827-v1.1.0.0` uses the earlier binary
  and was cancelled as superseded during its Paid overlay cycles. It is
  incomplete diagnostic evidence, not a pass or an application failure. Launcher
  cleanup recorded zero remaining sandbox processes/windows. Re-run the
  candidate screenshot and performance gates before producing release outputs.
- Fresh final UI runs passed: Paid `20260906-164203-v1.1.0.0` 71/71,
  Free `20260906-215030-v1.1.0.0` 62/62, 55 screenshots total. Paid overlay
  screenshot confirms all three header Refresh controls and unclipped actions.
  Both launchers cleaned up their hidden sandbox processes. The final frozen
  binary comparison follows these UI runs; no additional feature work is planned
  for this candidate.

## 2026-09-07 - v1.1.0.0 candidate handoff

- Final full comparison `20260906-223927-v1.1.0.0` passed both editions:
  20 measured main and overlay cycles each, complete coverage, enforced
  original budgets, zero budget failures. Paid retained deltas: main 14.59 MiB,
  +4 handles/-2 threads; overlay 12.08 MiB, +10/-2. Free: main 12.46 MiB,
  +8/+2; overlay 6.56 MiB, -4/-6. Margins and single-startup measurement limits
  are recorded in the performance closeout. No target GC or raised limits.
- Built all four v1.1.0.0 Lite/Portable outputs and both editions' MSIX and
  MSIXUPLOAD files. Release validation passed 39/39 checks, including version,
  identity, reserved display name, exact inner-package hashes and no E2E service
  marker. Recorded SHA-256 hashes for all eight outputs.
- Added the version-labelled download/evidence index in `RELEASE_1_1_0.md`.
  Automated candidate work is complete. User hardware acceptance, Store
  install/update checks and submission remain pending; no Git push performed.

## 2026-09-07 - v1.1.1.0 shortcut editor and audio actions

- Started the command-expansion development version without changing the
  frozen v1.1.0.0 packages. Added categorized, searchable built-in shortcut
  rows. Fixed an accessibility defect discovered by real WPF automation:
  filtering now collapses stable row containers rather than replacing their
  item source and losing accessible children.
- Editor-only isolated UI gates: Paid 73/73 (30 screenshots), Free 64/64
  (27 screenshots). Both lifecycle reports confirm background mode and zero
  remaining Sandbox processes/windows. These runs precede the audio editor
  extension and are not its visual sign-off.
- Added safe output volume up/down, output mute and microphone mute execution,
  with fresh default resolution, clamping, cancellation, no overlapping queue,
  and a 150 ms repeat guard. Tests use fake audio endpoints only.
- Added stable-key command bindings alongside unchanged legacy fields/IDs,
  transactional native rollback, accessible Set/Unset rows and a persisted
  1-20 percent volume step. Reset commits settings only after registration
  succeeds. Paid deterministic suite passes 168/168 after integration.
- v1.1.1.0 remains in development: target-specific favorites/profile navigation,
  endpoint role shortcuts, full action E2E and performance/release gates remain.

## 2026-09-08 - audio native-hotkey validation follow-up

- The Paid audio-editor UI run `20260907-215342-v1.1.1.0` passed
  78/78 checks with 31 screenshots. It did not yet press the new global chords.
- Free run `20260907-215955-v1.1.1.0` showed persisted assigned chords, but
  failed the first keypress check: SendKeys playback produced no simulated
  endpoint-volume journal entry. This is an unresolved execution-test failure,
  not evidence that the audio action passed. Cleanup left no Sandbox processes
  or visible windows.
- Added keyboard-stream injection to the Sandbox-only test helper, always
  releasing modifier keys. The immediate retry was blocked by an execution
  approval usage limit; a subsequent retry started after that limit reset.
  Application behavior remains unchanged pending the new result.
- The September 8 retry reached all four native actions after changing only
  the keyboard-injection method. The simulated journal recorded output volume
  0.72 -> 0.77000004 -> 0.72, output mute on playback-speakers, and microphone
  mute on recording-mic. This isolates the earlier missing action to SendKeys
  playback in the harness, not application command routing. Full suite results
  are recorded separately after overlay/theme validation and cleanup.
- Free `20260908-210631-v1.1.1.0` completed 73/73 checks with 28 screenshots,
  including all four native audio commands. The lifecycle report confirms
  background mode, zero remaining Sandbox processes and zero visible windows.
- Paid `20260908-211202-v1.1.1.0` completed 82/82 checks with 31 screenshots,
  including the same four native audio commands. Its lifecycle report also
  confirms background mode and complete process/window cleanup. Both final
  runs therefore provide 59 screenshots and zero failed checks. Free regression
  suite was rerun with the final profile-conflict assertion: 168/168 passed.
- No production release packages were generated and no host hardware settings
  were changed. Remaining targets/editor flows and full performance/release
  acceptance are tracked in `MILESTONE_1_1_1.md`.

## 2026-09-09 - overlay click-away dismissal

- Removed the global X above the overlay launcher. Empty workspace/root/canvas
  clicks now follow the existing close path, which captures layout before
  dismissal. The workspace previously swallowed those background events.
- Dismissal checks the original hit surface: events from widgets, controls,
  inspector, launcher and feedback content cannot accidentally close the
  workspace. Drag/resize handling and Esc navigation remain unchanged.
- Retained widget Remove and inspector Close controls, which have distinct
  purposes. Updated the Sandbox parity contract and added real background-click
  validation. The source version remains v1.1.1.0.
