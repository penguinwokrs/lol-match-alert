# Keyboard match alert

Light up the keyboard the moment League of Legends finds a match, and put it back the way it was
when the ready check is over.

## Goal

A player tabbed out of the League client misses the ready check. A Windows toast is easy to
miss too. The keyboard is the one thing in front of them, so this makes the keyboard flash.

| Must | Detail |
|---|---|
| Install like a normal app | Installer or portable zip. No Python, no .NET runtime, no command line |
| Work on the author's Keychron Q1 HE 8K | The first verified device |
| Be open to other makers and models | A new VIA keyboard needs no code; a new protocol is one driver class |
| Let users define the look | Color, pattern, step timing; built-in defaults, per-device and per-user overrides |
| Leave room for unknown keyboards | Detect what can be detected, ask the user for the rest, save it as a profile |
| Never leave the keyboard in a changed state | Restore on every exit path, and never write to EEPROM |

## Non-goals

- Accepting the ready check. The app reads client state only.
- Notifications other than the keyboard. The PowerShell toast script this replaces is removed.
- Localisation. English only in this change; the strings are kept in one place for a later `ja`.
- Drivers other than VIA. The driver seam exists; Razer, Logitech and others are future work.
- winget and scoop manifests. They need a published release to point at, so they follow the first
  release.

## Architecture

Clean architecture: dependencies point inwards, and the two outside worlds (the League client and
the keyboard) are behind ports that the core owns.

```
src/
  MatchAlert.Domain/   no dependencies
  MatchAlert.App/      -> Domain
  MatchAlert.Lcu/      -> Domain
  MatchAlert.Devices/  -> Domain, App
  MatchAlert.Tray/     -> all of the above (composition root, net10.0-windows WinForms)
tests/
  MatchAlert.Tests/
```

Every project except Tray targets plain `net10.0`, so the whole test suite runs on Linux and in CI.
Windows-only calls (P/Invoke into SetupAPI and HID) sit in Devices behind a transport interface and
are never reached by tests.

### Domain

| Type | Purpose |
|---|---|
| `Rgb`, `Hsv` | `#RRGGBB` parsing and conversion to the 0-255 hue/sat/value wheel keyboards use |
| `Step` | One frame: color, brightness percent, logical effect name, optional speed, duration |
| `Pattern` | Ordered steps plus `Repeat` (until stopped, or a count) |
| `GamePhase` | The gameflow phase string from the client |
| `AlertPolicy` | `ReadyCheck` starts an alert; any other phase, a disconnect, or the safety timeout stops it |
| `ILightingDevice` | `OpenSession()` returns an `ILightingSession` |
| `ILightingSession` | `Show(step)`; disposing restores what was there before |
| `IDeviceSource` | Enumerates the keyboards that can be lit right now |
| `IGameEvents` | An async stream of `GamePhase` changes, with connection state |

### App

| Type | Purpose |
|---|---|
| `AlertService` | Consumes `IGameEvents`, applies `AlertPolicy`, starts and stops playback on every device |
| `PatternPlayer` | Plays a `Pattern` on one session, step by step, on a `TimeProvider` |
| `Settings`, `SettingsLoader` | Built-in defaults, device profiles and the user's file merged into one validated model |
| `SettingsWatcher` | Reloads on file save; a broken file keeps the last good settings and raises an error |

### Lcu

Finds `LeagueClientUx.exe`, reads the `lockfile` beside it (`name:pid:port:password:protocol`),
opens `wss://127.0.0.1:<port>/` with basic auth `riot:<password>`, and subscribes to
`OnJsonApiEvent_lol-gameflow_v1_gameflow-phase`. Right after subscribing it also `GET`s
`/lol-gameflow/v1/gameflow-phase`, so starting the app during a ready check still alerts.

The client uses a self-signed certificate. Certificate checks are skipped for `127.0.0.1` only.

When the socket closes or the client exits, the stream reports `Disconnected` and the adapter
polls every few seconds for a new client. The lockfile is re-read on every connect because the
port and password change on every client launch.

Measured on a live client on 2026-10-04 (queue 420), `gameflow-phase` becomes `ReadyCheck` in the
same millisecond as `ready-check.state` becomes `InProgress` and `search.searchState` becomes
`Found`, and it fires once per match. After a decline the phase goes back to `Lobby` (or
`Matchmaking` when requeued). Custom games skip `ReadyCheck` entirely, so they never alert.

### Devices

```
Hid/          HidEnumerator, NativeMethods (from openinzone), RawHidTransport (flat 32-byte reports)
Via/          ViaProtocol, ViaKeyboard, ViaDriver, ViaSetup
DeviceProfile, ProfileStore, DriverRegistry, HidDeviceSource
```

`IDeviceDriver` lives here, not in Domain, because it speaks in HID terms:

```csharp
interface IDeviceDriver
{
    string Id { get; }                                    // "via"
    ILightingDevice Create(HidDeviceInfo hid, DeviceProfile profile);
    ISetupFlow? TrySetup(HidDeviceInfo hid);              // null: this driver cannot talk to it
}
```

`HidDeviceSource` enumerates HID collections, matches each against the loaded profiles
(vendor id, product ids, product string), and hands the matches to the profile's driver. A
keyboard that matches no profile is offered to the setup wizard instead.

## Settings

### Files

Built-in patterns and device profiles are embedded in the executable. The user's files live in
`%APPDATA%\lol-match-alert\`:

```
settings.json      patterns and which pattern each device uses
devices\*.json     device profiles; the setup wizard writes here
```

JSON with comments and trailing commas allowed, because people edit it by hand.

### Layers

Later layers win:

1. Built-in patterns
2. Built-in device profiles
3. User device profiles (same `id` replaces the built-in one)
4. `settings.json`

The pattern a device plays: `settings.devices[id].pattern`, else `settings.pattern`, else the
profile's `defaultPattern`, else `match-found`.

### settings.json

```json
{
  "pattern": "red-white-blink",
  "patterns": {
    "red-white-blink": {
      "repeat": "untilStopped",
      "steps": [
        { "color": "#FF0000", "brightness": 100, "durationMs": 300 },
        { "color": "#FFFFFF", "brightness": 100, "durationMs": 300 }
      ]
    }
  },
  "devices": { "keychron-q1-he-8k": { "pattern": "red-white-blink", "enabled": true } },
  "maxAlertSeconds": 30
}
```

| Field | Default | Rule |
|---|---|---|
| `color` | | `#RRGGBB`. The value channel scales brightness, so `#800000` is red at half brightness |
| `brightness` | 100 | 0-100 percent |
| `effect` | `solid` | A logical name the device profile maps to a number |
| `speed` | unchanged | 0-255, only meaningful on animated effects |
| `durationMs` | | Required when the pattern has more than one step |
| `repeat` | `untilStopped` | Or a positive count, after which the last step holds |
| `maxAlertSeconds` | 30 | Safety stop if the client never leaves `ReadyCheck` |

A single-step pattern is the "let the firmware animate it" case: write once, hold.

Validation is strict and reports the file, the field path and the reason. A broken file never stops
the app: it keeps the last good settings and shows the error from the tray.

### Built-in patterns

| Name | Look |
|---|---|
| `match-found` (default) | Red and white, alternating every 300 ms |
| `pulse` | Gold, firmware breathing effect |
| `steady` | Solid gold |

### Device profile

```json
{
  "id": "keychron-q1-he-8k",
  "name": "Keychron Q1 HE 8K",
  "driver": "via",
  "match": { "vendorId": "0x3434", "productIds": ["0x1010", "0x1011", "0x1012"], "productString": "Q1 HE" },
  "via": { "channel": 3, "resetOnEffect": true },
  "effects": { "solid": 1, "breathing": 2 },
  "minStepMs": 100
}
```

`minStepMs` is the shortest step the device can show cleanly. A faster user pattern is stretched
to it instead of failing. The Q1 HE value is set from the first hardware measurement, not assumed.

## Alert flow

```
phase -> ReadyCheck
  for each enabled device:
    open the HID handle, snapshot the four lighting values, persist the snapshot
    play the device's pattern
phase -> anything else  |  client disconnect  |  maxAlertSeconds  |  app exit
  stop playback, restore the snapshot, delete the persisted copy, close the handle
```

The HID handle is held only during an alert. Devices are enumerated per alert, which gives
hot-plugging for free and does not compete with the VIA app, Keychron Launcher or kbd-signal for
the collection the rest of the time.

### Restoring

- **Never persist.** Only VIA's set-value command is sent; save (`0x09`) is never sent. A power
  cycle always brings back the user's own lighting.
- **Every exit path restores.** Session disposal restores, and the tray's exit, session end and
  unhandled-exception paths dispose the running sessions.
- **Crash recovery.** The snapshot is written to `%LOCALAPPDATA%\lol-match-alert\pending\` before
  the first write and deleted after the restore. At startup a leftover snapshot is restored only
  if the keyboard still shows one of our pattern steps (same effect, hue and sat). Anything else
  means the user changed the lighting since, so the leftover is discarded and logged.

## VIA driver

The protocol knowledge is a C# reimplementation of what kbd-signal measured on real hardware,
including on this exact board.

### Transport

Raw HID usage page `0xFF60`, usage `0x61`. Output reports are report id `0x00` plus 32 bytes;
input reports the same. The firmware echoes every command. Reads are overlapped with a timeout,
because a synchronous `ReadFile` on Windows never returns when nothing arrives.

Windows delivers input reports to every open handle, so echoes of another process's commands
arrive here too. A response is accepted only when its leading bytes match the request; everything
else is discarded.

### Protocol

| | VIA v2 (protocol < 11) | VIA v3 (protocol >= 11) |
|---|---|---|
| set | `07 id data..` | `07 ch id data..` |
| get | `08 id` | `08 ch id` |
| ids | brightness `80`, effect `81`, speed `82`, color `83` | brightness 1, effect 2, speed 3, color 4 |

Protocol version comes from command `01`.

### Board quirks carried over

| Quirk | Handling |
|---|---|
| Some firmware resets color to red and brightness to full ~50-150 ms after an effect change | With `resetOnEffect`, drop brightness to 0 before the effect change, keep rewriting color while dark for 200 ms, confirm by read-back, then raise brightness |
| Brightness written right after an effect change can silently revert (seen up to ~300 ms) | After an effect change, rewrite brightness for 400 ms |
| v3 brightness does not round-trip (`44 -> 42`, exact only at 0 and 255) | Never compare brightness by equality on v3. Remember the (written, read-back) pair per device; a later snapshot that reads the read-back gets the written value, so restores do not walk the backlight down |

### Timing

Color and brightness writes during playback wait for their echo for 20 ms at most, so one
missed echo cannot stall a 300 ms blink. The long verified writes (up to 1.5 s) are used only for
effect changes and the restore.

Within a pattern, the effect is written only when it changes between steps. A red-white blink on a
solid effect is color writes only, and never triggers the reset quirk.

## Setup wizard

Opened from the tray, or offered automatically when a raw HID keyboard matches no profile.

1. List raw HID `0xFF60` interfaces that no profile matches. The user picks one.
2. Ask each driver's `TrySetup`. Today only VIA answers.
3. VIA: read the protocol version. On v3, verify channel 3 with a speed round trip (write a probe
   value, read it back, put the original back). Channel 3 is QMK's rgb_matrix channel and what
   Keychron uses.
4. If the round trip fails, stop. Writing to other channels is not safe: on a Q1 HE a write to
   channel 0 changed the effect and wedged the HID handle. The wizard shows "not supported yet"
   and a "copy device info" button for an issue report.
5. Detect `resetOnEffect` by changing the effect and watching for the reset's signature.
6. Show effects one by one and ask "steady / pulsing / neither" until both a steady and a pulsing
   effect are found. The enabled-effect list cannot be read over VIA, so only the user can say.
7. Restore the lighting, show the profile, and save it to `devices\<id>.json`.

The flow is a class in Devices that talks to an `IUserPrompt`. The tray implements the prompt with
a WinForms dialog; tests implement it with scripted answers.

## Tray

| Item | Action |
|---|---|
| Status | Waiting for League client / Connected / Match found |
| Keyboards | The recognised keyboards and their pattern |
| Test lighting | Plays the pattern for 3 s on every enabled keyboard |
| Set up a keyboard... | The setup wizard |
| Open settings folder | Explorer at `%APPDATA%\lol-match-alert` |
| Start with Windows | Toggles `HKCU\...\Run` |
| Exit | Restores anything lit, then exits |

The icon is drawn at runtime and changes color while alerting. One instance at a time (named
mutex). A log goes to `%LOCALAPPDATA%\lol-match-alert\log.txt`, rotated at 1 MB.

`lol-match-alert.exe --test` runs one cycle headless and exits: snapshot, one solid step for one
second, restore, read back, and log the before and after values. It is how a restore is verified
without a screen, and what the hardware check in this change uses.

## Error handling

| Failure | Behaviour |
|---|---|
| League client not running | Status says so; poll for it |
| Socket drops mid alert | Stop and restore; reconnect |
| Keyboard unplugged mid alert | That session ends; others carry on; logged |
| HID write fails | Counted as a miss; playback continues; restore still attempted |
| Settings file broken | Last good settings stay; tray shows the error |
| Second instance | Exits quietly |

## Testing

| Layer | How |
|---|---|
| Domain | Color conversion, pattern validation, policy transitions |
| App | Settings layering and validation errors; `PatternPlayer` step timing and stretching on a fake `TimeProvider`; `AlertService` start/stop against fake events and devices |
| Lcu | Lockfile parsing; frame parsing against `gameflow-phase` frames recorded from the live client (they carry no personal data) |
| Devices | A byte-level VIA simulator behind the transport interface: v2/v3 framing, foreign-echo discarding, the reset quirk, the lossy brightness round trip, channel verification, and the wizard with scripted answers |
| Hardware | `--test` on the Q1 HE 8K, comparing the four values before and after |

## Distribution

- `dotnet publish` of the tray: `win-x64`, self-contained, single file, compressed. Not trimmed:
  WinForms does not support trimming.
- Inno Setup installer, per user (no elevation), optional start with Windows. Same pattern as
  openinzone.
- A tag `v*` builds and tests on `windows-latest` and attaches the installer and a portable zip to
  the GitHub release. CI builds and tests on every push and pull request.

## Credits

The VIA protocol details, the Q1 HE reset quirk and the brightness round-trip measurements come
from [kbd-signal](https://github.com/Sora-bluesky/kbd-signal) (MIT). This is a reimplementation of
its findings in C#, credited in the README and NOTICE.
