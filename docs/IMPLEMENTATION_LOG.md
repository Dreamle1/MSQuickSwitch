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

- Detect topology/capabilities before enabling choices.
- Run the attended one- and two-monitor matrix.
- Add current-topology reporting once `QueryDisplayConfig` mapping is in place.
