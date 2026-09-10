# Windows Sandbox end-to-end testing

The Sandbox harness runs the real WinQuickSwitch WPF application in a disposable
Windows desktop. It uses the production windows, shared controls, coordinator,
overlay, widgets, persistence, and theme resources while replacing Windows
hardware adapters with deterministic test services. Windows UI Automation can
therefore execute feature actions without changing the host computer.

Production outputs never contain the simulated services. They are compiled only
for this harness with `WinQuickSwitchE2E=true` and activated only by the guest's
`--sandbox-e2e` argument.

## Host requirements

- Windows 11 Pro, Enterprise, or Education with virtualization enabled.
- The Windows Sandbox optional feature.
- Enough free space to publish a self-contained win-x64 test executable.

If Sandbox is not available, enable the Windows feature named
Containers-DisposableClientVM from "Turn Windows features on or off." Windows
may require one restart after the feature is enabled.

## Run the suites

From the repository root, run:

~~~powershell
.\tools\WindowsSandbox\Launch-WinQuickSwitchSandbox.ps1 -Edition Paid
~~~

For the Free edition:

~~~powershell
.\tools\WindowsSandbox\Launch-WinQuickSwitchSandbox.ps1 -Edition Free
~~~

The launcher builds a fresh self-contained E2E executable from the current
working tree. `-SkipBuild` reuses only the edition/version-specific E2E cache;
it never substitutes a production Portable artifact. For attended inspection,
combine `-ShowSandboxWindow -KeepSandboxOpen`; background runs always clean up
their hidden Sandbox client.

Windows Sandbox does not provide a supported headless mode and ignores ordinary
process startup window-state requests after its remote session connects. The
launcher therefore uses a host-side background controller: it hides only the
newly created Windows Sandbox and Windows Sandbox Client windows throughout the
run. Screenshots are rendered directly from each guest application window and
checked for non-blank content, so they do not depend on the viewer being visible
or foregrounded. Use `-ShowSandboxWindow` only for an attended run.

The guest writes `results.json` and stays connected. After that file is readable,
the host hides and terminates only the client processes created for that run.
It does not request an in-guest shutdown or close the RDP viewer interactively;
both paths can surface connection error `0x80072746` after a successful test.
Each unattended run also writes `host-lifecycle.json`, including the number of
Sandbox-owned windows hidden and the remaining process/window counts after
cleanup. Both remaining counts must be zero.

Validate the scripts without opening Sandbox:

~~~powershell
.\tools\WindowsSandbox\Test-WindowsSandboxHarness.ps1
.\tools\WindowsSandbox\Launch-WinQuickSwitchSandbox.ps1 -Edition Paid -SkipBuild -ValidateOnly
~~~

## Performance suites

Performance mode uses the same isolated app build, simulated services, and UI
Automation paths, but measures the work instead of running the mutation
coverage matrix. Run one edition with:

~~~powershell
.\tools\WindowsSandbox\Launch-WinQuickSwitchSandbox.ps1 -Edition Paid -Performance
~~~

Compare Paid and Free in consecutive fresh Sandbox sessions with:

~~~powershell
.\tools\WindowsSandbox\Run-SandboxPerformanceComparison.ps1
~~~

The default run performs two warm-up iterations and twenty measured iterations.
Use `-PerformanceWarmupIterations` and `-PerformanceIterations` on the launcher,
or `-WarmupIterations` and `-Iterations` on the comparison runner, to change the
sample count. Each edition measures:

- cold process-to-ready startup for the normal window and overlay;
- navigation to every edition-supported feature;
- overlay inspector open and return-to-workspace latency;
- every safe deterministic feature action, including Display modes, Audio
  roles/favorites/session controls, radio changes, Settings routes, floating
  widget lifecycle, Options state changes, legal dialogs, and Paid profile
  management;
- Display, Audio, and Devices refresh completion through the deterministic
  service journal;
- private memory, working set, peak working set, handles, threads, and process
  CPU time at 100-millisecond intervals across the complete repeated workload.

All Windows mutations run against deterministic E2E services inside the
Sandbox. The performance suite never changes host display topology, audio
defaults, radios, taskbar state, or startup registration. Native tray-menu
timing remains attended because Windows UI Automation cannot provide a stable
isolated timing boundary for that shell-owned surface.

Diagnostic screenshots are captured before and after each workload, but their
capture time is explicitly excluded from every timed stage. Warm-up stages are
retained in the raw report and excluded from median and P95 calculations.

Each edition writes to:

    artifacts\sandbox-performance\<edition>\<timestamp>-v<version>\results

`performance.json` contains raw timings, iteration-tagged process samples,
stage summaries, session summaries, edition-aware coverage, and budget results. The Paid/Free
comparison runner also writes `performance.html`, `performance-summary.csv`,
`memory-summary.csv`, and `coverage-summary.csv` under
`artifacts\sandbox-performance\comparison`.

The memory report includes initial/final boundaries after a 1.4-second settling delay, peak and retained
private memory, peak working set, sample counts, handle/thread deltas, and CPU
time for both the main-window and overlay processes. It also compares median
resource use across disjoint early and late windows of up to three measured
iterations. Budgets continue to enforce the latest boundary reading delta: 15 MiB of
private memory, 10 handles, and 2 threads. Trend is additional diagnostic
evidence, not a replacement for these limits. For fewer than six iterations,
windows shrink so they never overlap; a single iteration has no independent
trend (`ResourceTrendAvailable=false`). A flat trend does not establish that a
retained-resource failure is harmless initialization.
Missing measured-iteration samples fail coverage. The settling delay is not
proof of quiescence; final readings are not median-filtered, so a late increase
remains visible to the budget.

Run `tools\WindowsSandbox\Test-PerformanceAnalysis.ps1` for isolated fixture
checks of resource thresholds, warm-up exclusion, short-run windows, and empty
coverage. The main harness validator also runs these checks. This test loads
only analysis functions and does not start Sandbox or change Windows settings.

Performance budgets are provisional while the baseline is being established.
They are always reported, but do not fail a run unless
`-EnforcePerformanceBudgets` is supplied. This separates functional failures
from noisy host load while still making regressions visible. Use identical host
power settings, close competing workloads, and compare multiple runs before
changing a release budget.

## Coverage

`FeatureCoverage.json` is the executable coverage contract. The suite checks
that every edition-supported control is reachable and that service mutations
appear in `service-journal.jsonl`. Each interactive feature must also declare
its required performance stages or an explicit reason why it cannot be timed.
Missing edition-required stages fail the performance run even when provisional
latency and memory budgets are not enforced.

| Feature | Automated E2E coverage |
| --- | --- |
| Shell | Main window, pin, edition navigation, Ctrl+number routes, overlay handoff, launcher arrows/Escape, launcher footer |
| Display | Refresh, all four modes, topology state, Display Settings route, floating widget |
| Audio | Inventories, role selection, favorites, app volume/mute, mixer and Sound Settings routes, floating widget |
| Devices | Inventory, Wi-Fi/Bluetooth state, refresh, five Settings routes, floating widget |
| Options | General-shortcut section, taskbar, theme, overlay, pin, startup, reset/default controls |
| Favorites | Add/remove endpoint favorites and both favorite-shortcut sections inside Audio/Options |
| Shortcuts | General shortcut editor presence plus chord, registration, conflict, and overlay-action contracts |
| Tray | Version-4 native callback decoding contract; Open/Exit remain an attended shell smoke check |
| Persistence | Widget, floating-window, overlay-layout, migration, overlap, clamping, and edition contracts |
| Profiles | Paid seeded profiles, Apply, Copy/save, Undo, edition exclusion |
| About | Paid/Free identity, EULA, privacy, responsible-use content |
| Workspace | Dark/light live theme, deterministic High Contrast visual captures, every launcher inspector, widget workspace, old-title regression, and a 920x700 constrained responsive layout |
| Accessibility | Visible keyboard controls require UI Automation names; High Contrast checked-label screenshots receive a rendered-pixel contrast check |

The fixture includes multiple displays, playback and recording endpoints, app
volume sessions, Bluetooth and wired devices (including warning/present states),
both radios, taskbar/startup states, and two Paid profiles. Names are arbitrary
and contain no personal device information.

## Outputs

Each run writes to:

    artifacts\sandbox-ui\<edition>\<timestamp>-v<version>\results

The result directory contains:

- index.html: a local screenshot gallery.
- results.json: pass/fail checks and screenshot dimensions.
- FeatureCoverage.json: the edition-aware feature/control contract used by the run.
- service-journal.jsonl: every simulated Windows-service request in order.
- contract-tests.txt: service, hotkey, tray, persistence, geometry, and migration test output.
- sandbox.log: timestamped execution details.
- host-lifecycle.json: host-side background-window and cleanup evidence.
- PNG files for the conventional window and overlay surfaces.

Performance runs add `performance.json` and use the corresponding
`artifacts\sandbox-performance` root. A combined Paid/Free run adds the HTML and
CSV comparison files described above.

Performance runs also append `resource-iterations.jsonl` after every full
main-window or overlay cycle. Each record contains target-process readings
before and after collecting unused UI Automation proxies in the PowerShell
test client. This cleanup is outside action timing and does not force garbage
collection in WinQuickSwitch. Baseline/final retained-resource limits are
unchanged; these per-cycle records are diagnostic evidence, not a replacement
for the original memory, handle, or thread gates. An interrupted run's records
can help investigation but do not make that run a pass.

The complete payload and generated .wsb configuration remain beside the result
folder for reproducibility. The artifacts directory is ignored by Git.

## Isolation boundary

The .wsb configuration disables networking, clipboard redirection, microphone,
camera, printers, and virtual GPU access. The application payload is mapped
read-only. Only the dedicated result directory is writable from the Sandbox.
The runner copies the executable into the disposable desktop before launching
it and stores settings only in that temporary user profile.

Sandbox still cannot prove physical behavior. Keep attended checks for mixed-DPI
and negative-coordinate monitors, actual projection changes, endpoint routing,
Bluetooth/USB hot-plug, Store package installation/update, and shell timing.
Those checks are smaller than the automated suite and must preserve monitor
placement and rotation.

The constrained-work-area screenshot is a responsive-layout regression check,
not a substitute for physical 100%, 150%, and 200% DPI validation. High
Contrast screenshots use a test-build-only palette request because changing
the guest's full Windows accessibility theme can block its remote desktop
session. Production builds contain no marker hook and continue to use
`SystemParameters.HighContrast`. The visual captures and UI Automation-name
audit reduce the attended surface but do not replace a real Windows High
Contrast and screen-reader pass. Native tray Open/Exit remains an attended
shell check because the menu is owned by the Windows notification area.
