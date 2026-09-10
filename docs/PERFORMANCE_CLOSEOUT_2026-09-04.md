# v1.1.0 development: performance closeout, September 4, 2026

Executable version remains **v1.0.33.0**. The release resource gate is open.

## Current validation

- Isolated application regression tests: **149 passed, 0 failed**.
- Performance-analysis fixtures: **20 passed, 0 failed**, independently on
  Windows PowerShell 5.1 and PowerShell 7.
- Sandbox harness validation: passed.
- Latest completed 20-iteration performance run: feature coverage passed for
  both editions; resource budgets failed in both main-window sessions.
- Subsequent 20-iteration run: interrupted during Paid, with no completed
  result. It must not be counted as a pass or an application crash.

| Latest completed 20-iteration run | Private-memory delta | Handle delta | Thread delta | Resource gate |
| --- | ---: | ---: | ---: | --- |
| Paid main | +8.35 MiB | +13 | -2 | Fail: handles |
| Paid overlay | +3.28 MiB | +8 | -2 | Pass |
| Free main | +15.31 MiB | +13 | -1 | Fail: memory and handles |
| Free overlay | +8.05 MiB | -5 | -7 | Pass |

Limits remain +15 MiB private memory, +10 handles, and +2 threads. Speed
budgets passed in this completed comparison. These are deterministic Sandbox
workloads, not physical hardware or Valorant validation.

## Corrections to the test harness

Audio volume mutation now waits for the shared row to be ready before starting
the mute timing. Baseline/final readings follow a short settling delay and use
the latest actual reading, so a late resource increase is not median-filtered.
Iteration-labelled samples expose trends, with disjoint windows and explicit
unavailability for a one-iteration run. Retained-resource thresholds continue
to decide the resource gate; a flat trend cannot erase a failure.
Missing measured-iteration samples fail coverage; unavailable trends are null.

The six-iteration qualification's original green result used a temporary
trend-only gate. Under restored thresholds its Free main session fails with
12 retained handles. Historical reports are preserved, not rewritten.

## Evidence

- [Completed 20-iteration comparison, v1.0.33.0](../artifacts/sandbox-performance/comparison/20260903-233448-v1.0.33.0/performance.html)
- [Six-iteration qualification, v1.0.33.0 — provisional gate, see correction above](../artifacts/sandbox-performance/comparison/20260904-014637-v1.0.33.0/performance.html)
- [Paid UI screenshots, v1.0.33.0](../artifacts/sandbox-ui/paid/20260903-214314-v1.0.33.0/results/index.html)
- [Free UI screenshots, v1.0.33.0](../artifacts/sandbox-ui/free/20260903-214827-v1.0.33.0/results/index.html)

## Next work

Capture per-iteration retained resources and handle types in the isolated
process to identify the source of growth. The current measurements do not
establish whether it is lazy initialization, automation overhead, or an
application leak. Fix or explain that source before another full release run;
do not raise thresholds solely to obtain a green report. Attended physical
display/DPI/hot-plug and Store install/update gates also remain outstanding.

## September 5 continuation

Client-only UI Automation cleanup now preserves before/after readings for
each complete iteration. It does not establish a target-process leak source:
retained handle counts are non-monotonic, and immediate before/after readings
often match. No application garbage collection was forced.

The [Free six-iteration diagnostic, v1.0.33.0](../artifacts/sandbox-performance/free/20260904-212945-v1.0.33.0/results/index.html)
passed the original gates. The subsequent
[full 20-iteration comparison, v1.0.33.0](../artifacts/sandbox-performance/comparison/20260905-010403-v1.0.33.0/performance.html)
passed all retained-resource limits in both editions:

| Session | Private delta | Handles | Threads |
| --- | ---: | ---: | ---: |
| Paid main | +13.26 MiB | +6 | 0 |
| Paid overlay | +0.11 MiB | +4 | -3 |
| Free main | +14.12 MiB | +1 | -2 |
| Free overlay | +11.48 MiB | +3 | -3 |

Paid passed overall. Free failed only overlay startup: 2527.466 ms versus the
unchanged 2500 ms limit. Both editions completed all expected feature stages
and measured iterations. This is not an overall release pass.

This comparison used cached applications built before the September 5 visual
fixes and deferred widget construction. Fresh evidence is required for those
changes. Hardware acceptance was confirmed not yet tested by the user.

## Final v1.1.0.0 candidate - September 6 results

The [frozen-candidate comparison](../artifacts/sandbox-performance/comparison/20260906-223927-v1.1.0.0/performance.html)
**passed in both editions** with budget enforcement enabled, zero budget
failures, complete feature coverage and all 20 measured iterations in each
main/overlay session (plus two warm-ups). These results supersede the pending
automated-performance status above, not the historical measurements.

| Session | Retained private delta | Handles | Threads |
| --- | ---: | ---: | ---: |
| Paid main | +14.59 MiB | +4 | -2 |
| Paid overlay | +12.08 MiB | +10 | -2 |
| Free main | +12.46 MiB | +8 | +2 |
| Free overlay | +6.56 MiB | -4 | -6 |

The original retained limits remain 15 MiB, 10 handles and 2 threads. Paid's
main memory and overlay handles are close to those limits; a pass is not proof
that all hardware or longer-duration workloads are leak-free. No target-app
garbage collection was forced and no acceptance limit was raised.

Startup was Paid main 1440.06 ms / overlay 1924.86 ms, Free main 1436.65 ms /
overlay 1813.89 ms. Both timing gates passed. Each startup value represents the
single startup in that session, not a 20-launch distribution. Screenshot time
is excluded from measured action latency.

Both hidden sandboxes closed with zero remaining processes/windows. Physical
mixed-DPI, hot-plug, tray and fullscreen-game acceptance still belongs to the
attended checklist; this run uses simulated services. Candidate packaging may
proceed, but Store submission remains pending that acceptance.
