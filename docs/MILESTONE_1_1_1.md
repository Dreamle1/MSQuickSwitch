# v1.1.1.0 - command and shortcut expansion

Status: **in development**, started September 7, 2026. The v1.1.0.0 candidate
and its hashes remain unchanged. No v1.1.1.0 Store or production release is
claimed. The central, native and Store-template versions now match 1.1.1.0.

## First slice

- [x] Reuse the existing edition-aware typed catalog and registration safeguards.
- [x] Categorize built-in shortcut rows into collapsible sections.
- [x] Search command names, categories and assigned key text without executing
      commands or registering hotkeys.
- [x] Preserve live row identity on ordinary state refresh. Search temporarily
      opens matching sections; clearing it restores local expansion choices.
- [x] Keep search state local to each surface and leave saved bindings untouched.
- [x] Add regression coverage for categorization, matching, empty results,
      expansion restoration, independent surfaces and refresh stability.
- [x] Verify rendered search behavior in the isolated UI suite.

The search keeps existing WPF row containers and collapses non-matching rows
instead of replacing the item source. This preserves accessible Set/Unset
controls after filtering. The Paid v1.1.1.0 run dated September 7 verified the
matching button in the automation tree and captured the rendered search;
the complete Paid run passed 73/73 checks with 30 screenshots. Its lifecycle
report confirmed background mode and zero remaining processes or visible
sandbox windows after cleanup.

Editor-only evidence: [Paid v1.1.1.0 UI gallery](../artifacts/sandbox-ui/paid/20260907-192009-v1.1.1.0/results/index.html)
and [Free v1.1.1.0 UI gallery](../artifacts/sandbox-ui/free/20260907-192619-v1.1.1.0/results/index.html).
Free passed 64/64 checks with 27 screenshots and clean background cleanup.
These runs predate the assignable audio controls below.

This first search covers built-in command shortcuts. Favorites still use their
existing dedicated editors and Paid profiles remain in Profiles; integrating
those targeted rows into the search/editor is subsequent work, not completed.

## Audio shortcut slice

Audio execution is implemented and covered by nine deterministic
tests: volume up/down with clamping, output and microphone mute, fresh default
endpoint resolution, unavailable/ambiguous targets, cancellation, overlapping
calls, a 150 ms repeat guard and sanitized exception results. It uses existing
audio service contracts. Four assignable rows now route through the shared
command executor and stable-key native registration: volume up/down, output
mute and microphone mute. Options stores a configurable 1-20 percent step.
New bindings default to unassigned and use a separate native ID range; legacy
fields and IDs remain unchanged. Six native-adapter tests cover resolution,
order stability, duplicate owners, invalid chords, rollback and disposal.
Additional tests cover JSON compatibility, default step, reset and shared editor
commands. The combined suite passes 168/168 in both editions. Both production
Release builds have zero warnings/errors. No host audio was mutated.

The updated Paid UI run passed 78/78 checks with 31 screenshots, including all
four new audio rows and the step selector:
[Paid v1.1.1.0 audio editor gallery](../artifacts/sandbox-ui/paid/20260907-215342-v1.1.1.0/results/index.html).
This run verified controls, not keypress execution; the subsequent Free
scenario adds four native-hotkey checks against simulated endpoint logs.

The Free retry with native keyboard-stream injection passed 73/73 checks and
captured 28 screenshots. It verified volume up/down and both mute shortcuts
against the expected simulated endpoint IDs, and completed background cleanup:
[Free v1.1.1.0 native-audio UI gallery](../artifacts/sandbox-ui/free/20260908-210631-v1.1.1.0/results/index.html).
The previous SendKeys-only failure is retained as diagnostic evidence; it was
resolved by changing test input delivery, without changing application routing.

Paid completed the same native-keypress scenario: 82/82 checks and 31 screenshots,
with zero remaining Sandbox processes/windows after background cleanup:
[Paid v1.1.1.0 native-audio UI gallery](../artifacts/sandbox-ui/paid/20260908-211202-v1.1.1.0/results/index.html).
Together the final runs produced 59 screenshots. This completes the four-action
native execution check for both editions, not the remaining v1.1.1.0 release gates.

## Overlay dismissal polish

The redundant close button above the right launcher has been removed. Clicking
an empty overlay, workspace, or widget-canvas surface now dismisses the overlay;
clicking launcher entries, inspectors, widget cards, and their controls remains
interactive. Escape, inspector close, widget removal, and the explicit app Exit
command keep their existing meanings.

The Paid hidden Sandbox run passed 84/84 checks with 31 screenshots and clean
background cleanup. It includes native pointer validation of empty-background
dismissal and an automation check proving the removed close control is absent:
[Paid v1.1.1.0 overlay gallery](../artifacts/sandbox-ui/paid/20260909-173337-v1.1.1.0/results/index.html).
Both edition regression suites pass 168/168, and both Release builds complete
with zero warnings and zero errors.

## Remaining milestone work

1. Complete target-specific catalog routing, availability explanations and
   stable favorite/profile target handling.
2. Extend audio E2E coverage to capture/unset/reset, changing volume steps and
   simultaneous-surface feedback, then add endpoint-role shortcut targets.
3. Add Paid pinned-profile slots, next/previous and Undo shortcuts.
4. Complete conflict/owner presentation, category Reset and searchable
   favorite/profile rows. Retain transactional native registration rollback.
5. Add a sanitized printable shortcut summary, consistent accessible results,
   and migration/edition/failure tests.
6. Run complete Free/Paid UI, speed/memory and package gates, then produce
   version-labelled release outputs. Attended Windows checks still apply.

No arbitrary scripts/macros, destructive operations, host hardware mutations
during automated testing, or Bluetooth connect/disconnect actions are added.
