# WinQuickSwitch

[v1.1.0.0 test candidate: downloads, screenshots and results](docs/RELEASE_1_1_0.md).
Implementation and automated gates are complete; attended hardware acceptance
is still required before Store submission.

Current development: [v1.1.1.0 keyboard shortcuts](docs/MILESTONE_1_1_1.md).
The v1.1.0.0 candidate artifacts above remain frozen for testing.

WinQuickSwitch is a small Windows 10 and Windows 11 utility for reaching common display,
audio, microphone, Bluetooth, and wired-device controls without digging through
Settings.

This repository contains the product plan, technical design, and working
resident widget. Projection controls are wired to Windows, active audio
endpoints and application sessions stay current through Windows audio
notifications, and present Bluetooth/USB devices refresh when Windows reports
a hardware change.

## Planned first release

- Switch projection mode: PC screen only, duplicate, extend, or second screen
  only (the actions behind `Win + P`).
- View active per-application audio sessions and change their volume or mute
  state.
- View and choose speaker/headphone and microphone endpoints.
- List connected Bluetooth and locally wired devices, with connection and
  enabled-state information when Windows exposes it.
- Run as an ordinary user and remain resident but idle while hidden; no
  background service, account, telemetry, or network access.

The detailed scope and milestones are in
[docs/PRODUCT_PLAN.md](docs/PRODUCT_PLAN.md). Technical boundaries and API
choices are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Technology

- C# and WPF
- .NET 10 LTS
- Windows 10, version 2004 (build 19041) or newer, on x64
- Direct Windows APIs through small adapters
- No web view or Electron runtime

.NET 10 is used because it is the current active LTS release. A
framework-dependent build should be the default lightweight distribution;
single-file self-contained builds can be offered separately.

## Development setup

1. Install the .NET 10 SDK or Visual Studio 2026 with the .NET desktop
   development workload. The repository accepts .NET 10 feature bands starting
   at SDK `10.0.100` and rolls forward within .NET 10.
2. Confirm the SDK:

   ```powershell
   dotnet --info
   ```

3. Restore and build:

   ```powershell
   dotnet restore .\WinQuickSwitch.sln
   dotnet build .\WinQuickSwitch.sln
   ```

4. Run the application shell:

   ```powershell
   dotnet run --project .\src\WinQuickSwitch\WinQuickSwitch.csproj
   ```

5. Run the dependency-free automated tests:

   ```powershell
   dotnet run --project .\tests\WinQuickSwitch.Tests\WinQuickSwitch.Tests.csproj
   ```

A project-local SDK may also be installed in the ignored `.dotnet` directory.
The implementation and exact verification history are recorded in
[docs/IMPLEMENTATION_LOG.md](docs/IMPLEMENTATION_LOG.md).

## Using the resident widget

- Press `Win + Shift + Q` anywhere to show or hide WinQuickSwitch.
- In Options, enable **Use the dimmed sidebar overlay** to make `Win + Shift + Q`
  open the full-monitor **workspace overlay** on the monitor containing the
  pointer. It starts with Display, Audio, Devices, and Favorites quick cards
  in both editions. Profiles (Paid), About, and Options stay in the launcher
  until explicitly added as quick widgets. The right launcher opens the complete controls
  for Display, Audio, Devices, Favorites, Options, Profiles (Paid), and About;
  the redundant Home launcher entry is intentionally omitted. It is disabled
  by default so existing behavior is preserved.
- Workspace cards use the same complete feature controls as the normal window
  and right launcher. Each surface owns a separate WPF control instance while
  sharing one state, command, and refresh pipeline. Drag a card smoothly by its header;
  Audio, Devices, and Paid Profiles can also be resized from the corner.
  Positions and exact sizes are saved separately for each monitor. The ⟳ icon
  refreshes live feature state, the ↺ icon fits resizable content again, and
  the × icon removes a card until it is restored from the right launcher.
  Devices also has a —/□ minimize-and-restore control. Resizable widgets use
  their normal preset as the minimum expanded size (Devices is 840×820 DIP),
  unless the active monitor is physically smaller. Bottom-right resizing stops at the
  workspace edge without moving the card's left or top edge.
  Overlay cards do not have a Pin control and disappear when the workspace
  closes; the standalone floating widgets remain available separately.
- The workspace closes with `Esc` (first closes an inspector, then the
  workspace), the close button, the same shortcut, or a click on the dimmed
  background. `Ctrl + 0` returns to the quick widgets and `Ctrl + 1` through `Ctrl + 6`
  select the edition-aware feature routes. **Open main window** remains an
  optional fallback, and native Windows Settings links close the workspace
  before opening Settings.
- The first reveal opens beside the pointer and stays inside that monitor's
  work area. Later hide/show cycles keep the same position, including a
  position you dragged the widget to. If a taller panel would cross a monitor
  edge, the widget moves only enough to keep every control visible.
- Select a feature in the launcher to open another complete instance of the
  same feature view. Workspace cards remain optional, but they no longer use a
  reduced or separately maintained set of controls.
- The Display panel is the compact home surface: it keeps the four display
  actions visible and shows at-a-glance audio, device, and shortcut status.
- Use **Open overlay**, **Audio details**, or **Device details** from the normal
  window to move directly to the corresponding workspace experience.
- Use **Floating display** to detach the display actions into a resizable widget.
  Its size, position, and optional **Keep on top** state are restored safely on
  the next launch. **Reset layout** returns it to a safe default; **Back to main
  window** reattaches it.
- **Floating audio** exposes saved output and microphone favorites, while
  **Floating devices** exposes Wi-Fi and Bluetooth controls. Both have the same
  safe reset, pin, reattach, and work-area behavior.
- Press `Ctrl + 1`, `Ctrl + 2`, `Ctrl + 3`, or `Ctrl + 4` to switch panels.
- In Display, press `1` through `4` to request the corresponding projection
  mode.
- A display request is successful only after Windows reports the requested
  topology as stable. If a full-screen application or graphics driver restores
  another topology, WinQuickSwitch retries once, keeps the controls open, and
  reports the topology that remained active. Switching the application to
  borderless or windowed mode, or closing it, removes that external conflict.
- In Audio, press `O`, `I`, or `A` to focus output, input, or application
  sessions.
- Choose **Volume mixer** to open the full Windows per-app mixer. The **App
  volumes** section remains available for quick volume and mute changes without
  leaving WinQuickSwitch.
- Choose **Sound settings** for the general Windows **System > Sound** page.
- In Audio, select an output and choose **Favorite**. Up to four outputs can be
  saved; choose **Favorite** again to remove a saved output.
- In Devices, use the **Wi-Fi** and **Bluetooth** controls to request a radio
  state change. Windows may request permission the first time; if radio control
  is unavailable or denied, WinQuickSwitch opens the matching Settings page.
- The Devices panel also links directly to Wi-Fi, Bluetooth, Airplane mode,
  Network, and connected-device settings.
- Press `Esc`, click elsewhere, or close the window to hide it. Select
  **Quit** to exit the resident process.
- Use **Pin** in the main-window header (or **Pin the main window open** in
  Options) to keep the normal window visible and above other windows. Clear it
  to restore click-away hiding.
- Starting WinQuickSwitch again reveals the existing instance instead of
  starting a second resident process.
- In Options, select a shortcut field and press a modified letter, number, or
  function key. Choose **Unset** (or press Delete) to clear it. Separate global
  shortcuts can toggle the widget or open Display, Audio, or Devices directly.
- WinQuickSwitch keeps a native notification-area icon while resident. Left-click
  the icon or choose **Open main window** to reveal the widget; right-click it
  for **Open main window** and **Exit**. Opening from the tray also dismisses a
  visible overlay first, so the normal window cannot be left behind it.
- Expand **Display mode shortcuts** to assign a global shortcut to PC screen
  only, Duplicate, Extend, or second screen only. Display mode actions apply
  immediately without a confirmation prompt.
- Expand **Favorite output shortcuts** to assign a global shortcut to each
  saved output. Using one changes the normal Windows output without opening
  the widget; a failure opens Audio with an explanation.
- The Options theme toggle changes both the widget and supported Windows title
  bar colors immediately.
- The workspace Options inspector provides quick toggles for theme, overlay
  enablement, main-window pinning, start-with-Windows, taskbar auto-hide,
  shortcut reset, and monitor-layout reset. It also links to taskbar, display,
  and notification settings. Audio and Devices inspectors expose saved
  endpoint favorites, wireless controls, refresh, and settings links.
- In Options, **Show taskbar** turns off Windows auto-hide and **Hide taskbar**
  enables it. The status line reports the current state. **Taskbar settings**,
  **Display settings**, and **Notifications** open the related Windows pages
  for taskbar behavior, multiple displays, and notification-area controls.
- In Options, enable **Start WinQuickSwitch when I sign in** to register the
  current executable for your Windows account. Sign-in launches stay hidden
  and wait for a global shortcut.

The resident process keeps one shared service path. The Core Audio, display,
and device refresh gates are activated when either the normal window or the
workspace needs them; no duplicate watcher or display-topology pipeline is
created for the overlay.

Shortcuts and theme choice are saved to
`%LOCALAPPDATA%\WinQuickSwitch\settings.json`. The optional sign-in launch uses
the current user's Windows `Run` registration and does not require
administrator rights. Disable the checkbox before moving or deleting the
executable so Windows can remove that registration cleanly.

## Publishing

Two checked-in publish profiles keep release commands short and reproducible.
Current source builds target **v1.1.1.0** (development, not release-ready). The version is included in every
published folder and executable metadata so a downloaded file can be matched
to its build.

Free Lite build:

```powershell
.\.dotnet\dotnet.exe publish .\src\WinQuickSwitch\WinQuickSwitch.csproj -p:PublishProfile=Lite
```

Output when published: `artifacts\win-x64-free-lite-1.1.1.0\WinQuickSwitch.exe`

- Approximately 27.3 MB for the current resident-widget build.
- Requires the .NET 10 Desktop Runtime on the destination computer.
- Recommended when minimum download and app footprint matter most.

Free Portable build:

```powershell
.\.dotnet\dotnet.exe publish .\src\WinQuickSwitch\WinQuickSwitch.csproj -p:PublishProfile=Portable
```

Output when published: `artifacts\win-x64-free-portable-1.1.1.0\WinQuickSwitch.exe`

- Approximately 71.6 MB for the current resident-widget build.
- Includes .NET and runs without a separately installed runtime.
- Recommended for direct copying to an unknown Windows computer.

Both variants are Windows x64 single-file executables. Trimming and ReadyToRun
are intentionally disabled because this WPF/COM application prioritizes
correctness and compact output over speculative publish optimizations.

The standalone publish profiles are Free by default. Paid standalone builds
remain internal validation artifacts and can be selected explicitly with
`-p:WinQuickSwitchEdition=Paid`; their output folders use the `win-x64-paid-*`
prefix.

Microsoft Store package:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-StorePackage.ps1 -Edition Paid
```

Paid outputs:

- `artifacts\store-msix\paid\v1.1.1.0\WinQuickSwitch_1.1.1.0_x64.msix`
- `artifacts\store-msix\paid\v1.1.1.0\WinQuickSwitch_1.1.1.0_x64.msixupload`

The Free package uses the same builder and its separately reserved Partner
Center identity:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-StorePackage.ps1 -Edition Free
```

Free outputs are isolated under `artifacts\store-msix\free\v<version>`. Its
identity is `Dreamle.WinQuickSwitch-Free` and its reserved display name is
`WinQuickSwitch - Free` (including the spaces around the hyphen). Omitting
`-Version` uses `WinQuickSwitchReleaseVersion` from `Directory.Build.props`.

The Store package uses the reserved `Dreamle.WinQuickSwitch` identity. See
[store/README.md](store/README.md) for package details, versioning rules, and
the remaining certification work.

Before handing off a complete set of artifacts, run
`tools\Test-ReleaseArtifacts.ps1` to check executable versions, Store identity
and display names, upload/package consistency, and exclusion of simulated E2E
services. It does not install, run, sign, or upload packages. Physical acceptance
for the next release is tracked in
[v1.1.0.0 acceptance](docs/RELEASE_ACCEPTANCE_1_1_0.md); a test candidate is not a
Store-submission sign-off.

## Licensing

WinQuickSwitch is proprietary software and is not released under an open-source
license. Repository contents are covered by [LICENSE.txt](LICENSE.txt).
Customer use of a compiled Store release is covered by the
[End User License Agreement](docs/EULA.md) and applicable Microsoft Store
terms.

The publisher is Andrew Chang and the support contact is
`andrew.chang0@outlook.com`. Before commercial release, obtain legal review and
publish the final EULA and [privacy policy](docs/PRIVACY_POLICY.md).

See [Microsoft Store licensing guide](docs/STORE_LICENSING.md) for the proposed
paid-app model, entitlement flow, and remaining publication work.

## Repository layout

```text
docs/
  ARCHITECTURE.md     Windows API choices, boundaries, and risks
  IMPLEMENTATION_LOG.md  Completed work and verification evidence
  PRODUCT_PLAN.md     Scope, milestones, and acceptance criteria
src/
  WinQuickSwitch/     WPF application and Windows adapters
tests/
  WinQuickSwitch.Tests/  Dependency-free automated checks
```

## Isolated Windows end-to-end testing

The Windows Sandbox harness builds the current Paid or Free application,
launches the real WPF surfaces against deterministic simulated Windows
services, exercises every feature family, and exports a local PNG gallery,
service-action journal, coverage report, and pass/fail result:

~~~powershell
.\tools\WindowsSandbox\Launch-WinQuickSwitchSandbox.ps1 -Edition Paid
.\tools\WindowsSandbox\Launch-WinQuickSwitchSandbox.ps1 -Edition Free
~~~

Networking, clipboard, microphone, camera, and printer access are disabled.
Only the dedicated results directory is writable from the Sandbox. See
[Windows Sandbox E2E testing](docs/WINDOWS_SANDBOX_TESTING.md) for setup,
the feature matrix, outputs, and the smaller attended hardware-test boundary.

## Project status

Display switching, topology detection, audio controls, and connected-device
inventory are implemented. The resident slice adds a single-instance
resizable widget with a theme-matched native title bar and six mutually
exclusive panels. The default 900 x 720 layout exposes the full navigation and
larger device/audio lists, the window can be resized down to 600 x 420, and
vertical overflow scrolls on smaller screens. It places itself beside the
pointer once, then keeps a stable
position across hide/show. The default `Win + Shift + Q` shortcut and optional
direct panel, projection-mode, and favorite-output shortcuts are configurable
in Options. Up to four playback outputs can be saved as favorites. Closing or
clicking away hides the widget; **Quit** exits it. Hidden panels suspend their
refresh work and the audio watcher, so residency does not turn into continuous
polling. Optional per-user sign-in startup is implemented and starts hidden.
Tray integration, accessibility/DPI validation, and release signing remain M5
work. The native Windows title bar provides minimize, maximize/restore, and
drag-resize controls, and minimized windows remain available from the taskbar.
