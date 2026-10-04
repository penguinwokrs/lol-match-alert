# Pattern editor

Edit lighting patterns graphically, assign them to keyboards, and watch the result on the real
keyboard while editing.

## Goal

Patterns live in `settings.json`, which is fine for people who like JSON and a wall for everyone else.
This adds a window that makes a pattern something you see and drag, with the keyboard itself as the
preview.

| Must | Detail |
|---|---|
| Graphical | Steps are color blocks on a timeline whose width is their duration; drag to resize and reorder |
| See it as you edit | An on-screen preview plays the pattern at real speed, always |
| See it on the keyboard | A "Preview on keyboard" switch plays the pattern being edited on a chosen keyboard, live |
| Assign | Which pattern each keyboard plays, whether it plays at all, and the default |
| Safe | Closing or switching off puts the lighting back; a real match always wins over a preview |
| Honest about devices | Warn when a step is shorter than a keyboard can show, or uses an effect it lacks |

## Non-goals

- Other settings (safety stop, language, OpenRGB options). They stay in `settings.json`.
- Per-key lighting. Patterns are whole-keyboard colors, as now.
- Keeping `settings.json`'s comments. Saving rewrites the file; the previous one is kept as `.bak`.

## Window

Opened from the tray menu ("Edit patterns…"). Dark theme, matching OpenInzone's settings window.

```
┌ Pattern [match-found ▼] [New] [Duplicate] [Rename] [Delete / Reset]         ┐
│ Preview  ██████████████████████████  (plays the pattern at real speed)      │
│                                                                              │
│ Timeline (width = duration)                                   ⚠ notes       │
│ ┌────────┬────────┬───┐                                                      │
│ │ ██red█ │ ░white░ │ + │   drag the right edge to resize, a block to move    │
│ │ 300 ms │ 300 ms  │   │                                                     │
│ └────────┴─────────┴───┘                                                     │
│                                                                              │
│ Selected step                      │ Pattern                                 │
│  [color wheel]  #FF0000  [ ] Off   │  Repeat (● until the alert ends ○ [3]×) │
│  Brightness ────────● 100 %        │                                         │
│  Effect (● Steady ○ Breathing)     │ Keyboards                               │
│  Speed  ─────●───  (breathing)     │  [x] Keychron Q1 HE 8K   [default ▼]    │
│  Duration [300] ms  [◀][▶][Remove] │  [x] Logitech G (G HUB)  [pulse ▼]      │
│                                    │  Default pattern [match-found ▼]        │
│ [●] Preview on keyboard [Keychron Q1 HE 8K ▼]          [Save] [Cancel]      │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Built-in patterns** can be edited; that saves an override under the same name, as `settings.json`
  already allows. "Reset" removes the override. Built-ins cannot be deleted.
- **Deleting** a user pattern clears any assignment that used it, so the result always loads.
- **"Off"** makes a step black, which is how a pattern turns the lights off between flashes.
- **Durations** snap to 50 ms, from 50 ms to 10 s. A pattern with one step has no duration: the keyboard
  holds it.
- **Notes** show, per keyboard the pattern is assigned to or previewed on: steps shorter than its
  `minStepMs` (they will be stretched), effects it does not have, and devices that show breathing steady
  (Razer, OpenRGB) or are unverified.
- **Save** saves and closes; if saving fails the window stays open with the reason. **Cancel** closes
  without saving. Closing the window any other way asks about unsaved changes.

## Keyboard preview

While the switch is on, the chosen keyboard plays the pattern being edited:

- Every edit restarts playback with the new pattern **without restoring in between**: the session stays
  open, only the player restarts, so editing does not flicker back to the user's lighting.
- Switching off, choosing another keyboard, closing the window or exiting the app restores the lighting.
- **A real match wins.** The preview runs through the alert service, under the same lock as alerts. A
  ready check stops the preview, restores, and plays the real alert. The switch turns itself off and
  says why.
- A step with an effect the keyboard lacks previews as steady, with a note.

## Saving

`SettingsWriter` (App) edits `settings.json` as a JSON tree:

- It touches only `pattern`, `patterns` and `devices.<id>.pattern` / `devices.<id>.enabled`. Every other
  key survives.
- The result is validated with `SettingsLoader` against the profiles on disk before anything is written;
  a failure is shown and nothing is written.
- The previous file is copied to `settings.json.bak`, then the new one is written atomically.
- The settings watcher reloads it as it does for hand edits.

## Architecture

Logic that has nothing to do with WPF goes in App and is tested on Linux:

| Type | Purpose |
|---|---|
| `PatternDraft` | The pattern being edited: add, remove, move, resize, recolor; `ToPattern()`; `StepAt(elapsed)` for the on-screen preview |
| `PatternCheck` | Notes for a pattern on a device: too-short steps, missing effects |
| `SettingsWriter` | Reads, edits, validates and writes `settings.json` |
| `AlertService.StartPreview` | Returns a preview handle with `Update(pattern)` and `Dispose()`; preempted by a real alert |

Tray gets the WPF window: `PatternEditorWindow` and three controls, `TimelineControl`, `ColorWheel`
and the preview strip. The tray project adds `UseWPF`; WPF windows run on the existing WinForms message
loop, with `ElementHost.EnableModelessKeyboardInterop` for keyboard input.

Strings go in the existing resx files, English and Japanese.

## Testing

| What | How |
|---|---|
| `PatternDraft`, `PatternCheck` | Unit tests: operations, snapping and limits, `StepAt` across steps, repeats and holds |
| `SettingsWriter` | Unit tests: unrelated keys survive, built-in override and reset, deleting clears assignments, invalid results are refused, backup written |
| Preview | Unit tests with fake devices: one session for many updates, restore on stop, a ready check preempts it |
| Window | `lol-match-alert.exe --edit-patterns` opens the editor directly; screenshots on Windows to check layout in both languages |
| Hardware | Preview on the Keychron Q1 HE 8K: lights while on, restores on off and on close |
