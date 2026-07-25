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
