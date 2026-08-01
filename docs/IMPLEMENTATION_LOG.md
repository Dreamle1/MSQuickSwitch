# Implementation and test log

This file records completed slices, important decisions, and verification
evidence. Attended hardware checks are kept separate from automated tests so a
normal test run never changes display or audio state.

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
