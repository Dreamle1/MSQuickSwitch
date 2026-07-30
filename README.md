# WinQuickSwitch

WinQuickSwitch is a small Windows 11 utility for reaching common display,
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
- Windows 11, version 22H2 or newer
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
- The first reveal opens beside the pointer and stays inside that monitor's
  work area. Later hide/show cycles keep the same position, including a
  position you dragged the widget to. If a taller panel would cross a monitor
  edge, the widget moves only enough to keep every control visible.
- Select **Display**, **Audio**, **Devices**, or **Options** to show one compact
  panel at a time.
- Press `Ctrl + 1`, `Ctrl + 2`, `Ctrl + 3`, or `Ctrl + 4` to switch panels.
- In Display, press `1` through `4` to request the corresponding projection
  mode.
- In Audio, press `O`, `I`, or `A` to focus output, input, or application
  sessions.
- In Audio, select an output and choose **Favorite**. Up to four outputs can be
  saved; choose **Favorite** again to remove a saved output.
- Press `Esc`, click elsewhere, or close the window to hide it. Select
  **Quit** to exit the resident process.
- Starting WinQuickSwitch again reveals the existing instance instead of
  starting a second resident process.
- In Options, select a shortcut field and press a modified letter, number, or
  function key. Delete clears the field. Separate global shortcuts can toggle
  the widget or open Display, Audio, or Devices directly.
- Expand **Display mode shortcuts** to assign a global shortcut to PC screen
  only, Duplicate, Extend, or second screen only. The two modes that can turn
  off the current display still ask for confirmation.
- Expand **Favorite output shortcuts** to assign a global shortcut to each
  saved output. Using one changes the normal Windows output without opening
  the widget; a failure opens Audio with an explanation.
- The Options theme toggle changes both the widget and supported Windows title
  bar colors immediately.
- In Options, enable **Start WinQuickSwitch when I sign in** to register the
  current executable for your Windows account. Sign-in launches stay hidden
  and wait for a global shortcut.

Only the visible panel refreshes. The Core Audio notification watcher stops
when Audio is not visible, and hidden device events only mark the inventory for
refresh the next time Devices opens.

Shortcuts and theme choice are saved to
`%LOCALAPPDATA%\WinQuickSwitch\settings.json`. The optional sign-in launch uses
the current user's Windows `Run` registration and does not require
administrator rights. Disable the checkbox before moving or deleting the
executable so Windows can remove that registration cleanly.

## Publishing

Two checked-in publish profiles keep release commands short and reproducible.

Lite build:

```powershell
.\.dotnet\dotnet.exe publish .\src\WinQuickSwitch\WinQuickSwitch.csproj -p:PublishProfile=Lite
```

Output: `artifacts\win-x64-lite\WinQuickSwitch.exe`

- Approximately 335 KB for the current resident-widget build.
- Requires the .NET 10 Desktop Runtime on the destination computer.
- Recommended when minimum download and app footprint matter most.

Portable build:

```powershell
.\.dotnet\dotnet.exe publish .\src\WinQuickSwitch\WinQuickSwitch.csproj -p:PublishProfile=Portable
```

Output: `artifacts\win-x64-portable\WinQuickSwitch.exe`

- Approximately 64.9 MB for the current resident-widget build.
- Includes .NET and runs without a separately installed runtime.
- Recommended for direct copying to an unknown Windows computer.

Both variants are Windows x64 single-file executables. Trimming and ReadyToRun
are intentionally disabled because this WPF/COM application prioritizes
correctness and compact output over speculative publish optimizations.

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

## Project status

Display switching, topology detection, audio controls, and connected-device
inventory are implemented. The resident slice adds a single-instance
500-pixel widget with a theme-matched native title bar and four mutually
exclusive panels. It places itself beside the pointer once, then keeps a stable
position across hide/show. The default `Win + Shift + Q` shortcut and optional
direct panel, projection-mode, and favorite-output shortcuts are configurable
in Options. Up to four playback outputs can be saved as favorites. Closing or
clicking away hides the widget; **Quit** exits it. Hidden panels suspend their
refresh work and the audio watcher, so residency does not turn into continuous
polling. Optional per-user sign-in startup is implemented and starts hidden.
Tray integration, accessibility/DPI validation, and release signing remain M5
work.

## License

No open-source license has been selected yet. Until one is added, all rights
are reserved.
