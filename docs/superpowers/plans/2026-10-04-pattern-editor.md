# Pattern Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A graphical pattern editor with live keyboard preview and keyboard assignment. Spec: `docs/superpowers/specs/2026-10-04-pattern-editor-design.md`.

**Architecture:** Pure logic in App (tested on Linux): `PatternDraft`, `PatternCheck`, `SettingsWriter`, and preview support in `AlertService`. WPF window and controls in Tray, on the existing WinForms loop.

**Tech Stack:** .NET 10, WPF + WinForms (tray), xUnit.

## Global Constraints

- No NuGet dependencies in `src/`.
- `dotnet build MatchAlert.sln -warnaserror` and `dotnet test MatchAlert.sln` stay green on Linux and Windows CI.
- Every user-facing string in both `Strings.resx` and `Strings.ja.resx` (the parity test enforces it).
- Durations snap to 50 ms, 50 ms to 10 000 ms.

---

### Task 1: PatternDraft and PatternCheck (App)
- [x] Tests: add/remove/move/resize with snapping and limits; one-step pattern has no duration; `ToPattern` round-trips `FromPattern`; `StepAt` walks steps, loops until stopped, holds the last step after a count; `PatternCheck` flags steps shorter than `minStepMs` and effects a profile lacks.
- [x] Implement; commit `App: pattern draft and checks`.

### Task 2: SettingsWriter (App)
- [x] Tests: unrelated keys and their values survive; a user pattern is written; editing a built-in writes an override and Reset removes it; deleting a pattern clears `pattern` and `devices.*.pattern` that used it; enabled flags round-trip; a result `SettingsLoader` rejects is refused and nothing written; `.bak` holds the previous file.
- [x] Implement; commit `App: write settings.json from the editor`.

### Task 3: Preview in AlertService (App)
- [x] Tests: `StartPreview` opens one session per chosen device and plays; `Update` restarts playback without reopening sessions; `Dispose` restores; a ready check during a preview restores, raises `PreviewEnded`, and plays the real alert; a preview cannot start while an alert plays.
- [x] Implement; commit `App: live preview through the alert service`.

### Task 4: Editor window (Tray)
- [x] `UseWPF`; `PatternEditorWindow` (dark theme), `TimelineControl` (blocks, resize thumb, drag to reorder, add), `ColorWheel` (hue/sat disk + hex), preview strip on a `DispatcherTimer`.
- [x] Pattern list with New / Duplicate / Rename / Delete-or-Reset; repeat; keyboards panel with enabled + pattern + default; notes; keyboard preview switch and device choice; Save / Close with unsaved-changes prompt.
- [x] Tray menu item; `--edit-patterns` opens it directly; strings en/ja.
- [ ] Screenshots on Windows in both languages; hardware preview on the Q1 HE 8K; commit `Tray: pattern editor`.

### Task 5: Ship
- [ ] README (en/ja) sections; PR with verified / not verified.
