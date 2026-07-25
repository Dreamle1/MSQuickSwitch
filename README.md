# WinQuickSwitch

WinQuickSwitch is a small Windows 11 utility for reaching common display,
audio, microphone, Bluetooth, and wired-device controls without digging through
Settings.

This repository contains the product plan, technical design, and working
display, audio, and connected-device slices. Projection controls are wired to
Windows, active audio endpoints and application sessions stay current through
Windows audio notifications, and present Bluetooth/USB devices refresh when
Windows reports a hardware change.

## Planned first release

- Switch projection mode: PC screen only, duplicate, extend, or second screen
  only (the actions behind `Win + P`).
- View active per-application audio sessions and change their volume or mute
  state.
- View and choose speaker/headphone and microphone endpoints.
- List connected Bluetooth and locally wired devices, with connection and
  enabled-state information when Windows exposes it.
- Run as an ordinary user and stay closed when not needed; no background
  service, account, telemetry, or network access.

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

## Publishing

Two checked-in publish profiles keep release commands short and reproducible.

Lite build:

```powershell
.\.dotnet\dotnet.exe publish .\src\WinQuickSwitch\WinQuickSwitch.csproj -p:PublishProfile=Lite
```

Output: `artifacts\win-x64-lite\WinQuickSwitch.exe`

- Approximately 271 KB.
- Requires the .NET 10 Desktop Runtime on the destination computer.
- Recommended when minimum download and app footprint matter most.

Portable build:

```powershell
.\.dotnet\dotnet.exe publish .\src\WinQuickSwitch\WinQuickSwitch.csproj -p:PublishProfile=Portable
```

Output: `artifacts\win-x64-portable\WinQuickSwitch.exe`

- Approximately 62 MB.
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

Display switching, topology detection, and capability state are implemented.
Attended display-switch tests remain before M1 is signed off. Audio inventory
loads automatically, follows Windows notifications, and supports
per-application volume/mute plus separate normal and calls defaults. Compact
badges show which output and input currently hold each Windows role. M4 adds a
present-device inventory for Bluetooth and USB-wired hardware, collapses common
Windows profile/interface duplicates, refreshes on device-change messages, and
links to Windows Settings for pairing or removal. The compact 660-pixel window
uses a native dark title bar and keeps long inventories inside their own
vertical scrollers. The active display mode stays highlighted, redundant
requests are ignored, and a short post-change settle check follows Windows'
real topology without idle polling. M5 polish and release work is next.

## License

No open-source license has been selected yet. Until one is added, all rights
are reserved.
