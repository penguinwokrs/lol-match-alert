# lol-match-alert

English · [日本語](README.ja.md)

Your keyboard flashes the moment League of Legends finds a match, and goes back to your own lighting
when the ready check is over. Tab out, make tea, look at the keyboard.

> **Not affiliated with Riot Games.** This reads the League client's local, unofficial API. It never
> accepts the ready check for you and never changes what the client does.

## Install

Download `lol-match-alert-<version>-setup.exe` from
[Releases](https://github.com/penguinwokrs/lol-match-alert/releases) and run it. No administrator
rights, no runtime to install. It starts in the system tray and, if you leave the box ticked, with
Windows.

Prefer no installer? The `win-x64.zip` holds the same single exe.

## Use

Leave it in the tray. The icon tells you what it is doing:

| Icon | Meaning |
|---|---|
| Grey keys | Waiting for the League client |
| Blue keys | Connected, waiting for a match |
| Red keys | Match found: the keyboard is flashing |

The keyboard flashes from the moment the ready check appears until you accept, decline or it times
out, then your own lighting comes back. Custom games have no ready check, so they do not flash.

**Test lighting** in the tray menu (or a left click on the icon) flashes it for three seconds.

### Requirements

- Windows 10 1809 or later, x64.
- A keyboard **connected by USB cable**. Wireless, 2.4 GHz and Bluetooth do not carry the commands.
- Close the VIA app and Keychron Launcher while it runs: two programs changing the lights at once
  get in each other's way. [kbd-signal](https://github.com/Sora-bluesky/kbd-signal) can stay
  installed, but if both want the lights at the same moment, the last one wins.

Your keyboard's saved lighting is never touched. Every change lives in the keyboard's memory only,
so unplugging it always brings back your own settings.

## Keyboards

Support by maker. **Verified** means tried on a real keyboard; **unverified** means built from the maker's
own tools and specifications but not yet tried on one (owners can check in a minute, see
[Pulsar keyboards](#pulsar-keyboards)). Every keyboard must be connected by USB cable.

| Maker | Models | Support | How |
|---|---|---|---|
| Keychron | Q1 HE 8K | **Verified** | Built in |
| Keychron | Other models with QMK firmware (those that work with VIA or Keychron Launcher) | Unverified | Setup wizard |
| Keychron | Models without QMK firmware | Not supported | |
| Pulsar | PCMK 2HE TKL, XBOARD MS | Unverified | Built in |
| Pulsar | PCMK 3 HE 60, PCMK 3 HE TKL (SayoDevice firmware) | Unverified, may not light at all | Built in |
| Pulsar | Other models on the same firmware | Unverified, may not light at all | Setup wizard, no questions |
| Pulsar | Xboard QS with its VIA firmware | Unverified | Setup wizard |
| Pulsar | PCMK TKL (first generation) | Not supported | |
| Logitech G (Logicool G) | Every device G HUB lights: keyboards, mice, headsets, speakers | Unverified | Through G HUB (below) |
| Any maker | Keyboards with QMK firmware and VIA support | Unverified | Setup wizard |
| Any maker | Every device OpenRGB supports, if you run OpenRGB | Unverified, off until switched on | Through OpenRGB (below) |
| Razer, Corsair, SteelSeries, Wooting and others | Keyboards that only work with the maker's own software | Not supported | |

- **Setup wizard** means tray menu, *Set up a keyboard…*. It detects what it can, and for VIA keyboards
  asks which effects look steady and pulsing. VIA keyboards work when their per-key lighting is on VIA's
  standard lighting channel; the wizard checks that first, with one speed write it puts straight back,
  and says so if it is not.
- **Not supported** means no driver yet. Each maker's protocol is one driver (see
  [Adding another maker's protocol](#adding-another-makers-protocol)), and a
  [keyboard support issue](https://github.com/penguinwokrs/lol-match-alert/issues/new?template=keyboard-support.yml)
  with the wizard's *Copy details* output is the place to start.

### Logitech G

Logitech G devices are lit through **G HUB**, with Logitech's own LED SDK, rather than directly. G HUB
stays in charge: this app asks it to remember the lighting, shows the pattern, and asks it to put the
lighting back. Nothing is written to a device's memory, and when the app closes G HUB takes over again.

- G HUB must be installed and running. Without it, nothing Logitech appears and nothing happens.
- Every Logitech G device G HUB lights joins in. Set `"devices": { "logitech-g-hub": { "enabled": false } }`
  to leave them alone, or give them their own `"pattern"`.
- If nothing lights, check that G HUB lets games and apps control the lighting.
- Not tried on Logitech hardware yet. `--test` plays the pattern through G HUB and says whether G HUB
  accepted it, but G HUB cannot report its lighting, so there is no before-and-after to compare.

Nothing of Logitech's is included with this app: it uses the copy of the SDK that G HUB installs.

### OpenRGB

If you already use [OpenRGB](https://openrgb.org/), this app can light anything OpenRGB drives, through
OpenRGB's SDK server. It is off until you switch it on, because a keyboard driven both by OpenRGB and by
one of this app's own drivers would be fought over.

1. In OpenRGB, open the SDK Server tab and start the server (port 6742).
2. In `settings.json`, add `"devices": { "openrgb": { "enabled": true } }`.
3. If OpenRGB also drives a keyboard this app knows itself (a Keychron, say), switch one of them off for it,
   for example `"keychron-q1-he-8k": { "enabled": false }`.

Only keyboards flash by default, not the whole PC. To include more, copy
[`openrgb.json`](src/MatchAlert.App/BuiltIn/devices/openrgb.json) into `devices\` and change
`deviceTypes` (`keyboard`, `mouse`, `headset`, `ledstrip`, … or `"all"`). After the alert, OpenRGB gets
back the mode and colors it was showing. For most devices that is OpenRGB's own state, since OpenRGB cannot
read the hardware. OpenRGB has no generic breathing effect, so breathing steps show steady.

### Pulsar keyboards

Pulsar's current keyboards are not VIA keyboards: they are configured with Pulsar's web app,
Bibimbap, which speaks two different protocols depending on the model. Both are reimplemented here from
Bibimbap's own code.

- **PCMK 2HE TKL and XBOARD MS** use a lighting protocol of their own on the raw HID interface. They are
  recognised as soon as they are plugged in.
- **The PCMK 3 HE series** (60 and TKL) runs SayoDevice firmware, and is also recognised as soon as it is
  plugged in. Other Pulsar models on that firmware are set up by the wizard from the name the keyboard
  reports, by reading only; there are no questions. One thing is unknown here: Bibimbap always follows a lighting change with a "save all", which this app
  never sends. If the firmware only shows lighting once it is saved, the keyboard will simply not light.
  That is safe, but it is the first thing to check.

Nobody has tried either on a real board yet, so the app is careful: it never sends Bibimbap's "save"
command, never touches the bootloader, and by default plays the keyboard's own breathing effect so it
writes to the keyboard only once per alert.

**If you own one, you can verify it in a minute** (close Bibimbap first):

1. In a command prompt, run the line below. It flashes the keyboard and prints its lighting before and
   after; `restored` at the end means it came back.
   ```
   "%LOCALAPPDATA%\Programs\lol-match-alert\lol-match-alert.exe" --test
   ```
2. Unplug the keyboard and plug it back in. If your own lighting is there, the app's writes did not stick;
   if the app's gold is there, they did.
3. [Open an issue](https://github.com/penguinwokrs/lol-match-alert/issues/new?template=keyboard-support.yml)
   with what `--test` printed and what you saw after the replug. That is what it takes to mark it verified
   and allow faster patterns.

### Setting up a keyboard that is not listed

Tray menu, **Set up a keyboard…**. The wizard works out what it can by itself, then lights the
keyboard and asks how it looks ("steady", "pulsing", or "something else") until it knows which
effects to use. Usually two questions. Your lighting is put back afterwards, and the result is saved
as a profile in `%APPDATA%\lol-match-alert\devices\`.

If the wizard says the keyboard is not supported yet, press **Copy details** and
[open an issue](https://github.com/penguinwokrs/lol-match-alert/issues/new?template=keyboard-support.yml).

## Make it yours

Tray menu, **Open settings folder**, then edit `settings.json`. Changes apply the moment you save.
If something is wrong, the tray says what and where, and keeps using the previous settings.

```jsonc
{
  "pattern": "my-blink",
  "patterns": {
    "my-blink": {
      "steps": [
        { "color": "#00A0FF", "brightness": 100, "durationMs": 250 },
        { "color": "#000000", "durationMs": 250 }
      ]
    }
  },
  "devices": { "keychron-q1-he-8k": { "pattern": "pulse" } },
  "maxAlertSeconds": 30
}
```

| Field | Meaning |
|---|---|
| `pattern` | What plays on every keyboard: a built-in or one of yours |
| `patterns.<name>.steps` | Played in order, then again. One step is written once and held |
| `color` | `#RRGGBB`. `#000000` is off; darker colors are dimmer |
| `brightness` | 0-100, default 100 |
| `durationMs` | How long the step shows. Needed when there is more than one step |
| `effect` | `solid` (default) or `breathing`, or any name the keyboard's profile defines |
| `speed` | 0-255, for animated effects |
| `repeat` | `"untilStopped"` (default) or a number of rounds, after which the last step holds |
| `devices.<id>` | Per keyboard: `pattern` to override, `"enabled": false` to leave it alone. The id is in the tray menu under Keyboards (click to copy) |
| `maxAlertSeconds` | Safety stop, default 30 |
| `language` | Menu and wizard language: `"auto"` (follows Windows, default), `"en"` or `"ja"`. Applies on the next start |

Built-in patterns:

| Name | Look |
|---|---|
| `match-found` (default) | Red and white, swapping every 300 ms |
| `pulse` | Gold, breathing |
| `steady` | Solid gold |

A keyboard's profile can be overridden too: copy it into `devices\` with the same `id` and change
what you need. The built-in ones are in
[`src/MatchAlert.App/BuiltIn/devices`](src/MatchAlert.App/BuiltIn/devices).

## Troubleshooting

- **Nothing happens on a match.** Check the icon is blue while the client is open. If it stays grey,
  the client was not found. The log is `%LOCALAPPDATA%\lol-match-alert\log.txt`.
- **Test lighting says no keyboard is connected.** Use a cable, and check the keyboard is listed
  under Keyboards. If not, run the wizard.
- **Check a restore without looking.** `lol-match-alert.exe --test` plays the pattern, restores the
  lighting, reads it back and prints before and after. Exit code 0 means it came back as it was.

## How it works

```
League client --websocket--> Lcu ----IGameEvents----> App: AlertService --ILightingDevice--> Devices: VIA driver --raw HID--> keyboard
                                                       ^ settings.json, device profiles
```

The client exposes a local websocket (WAMP 1.0) on the port and password in its `lockfile`. The app
subscribes to one event, `OnJsonApiEvent_lol-gameflow_v1_gameflow-phase`, and alerts while the phase
is `ReadyCheck`. Measured on a live client, that event fires in the same millisecond as the ready
check, once per match.

For each keyboard it snapshots the lighting, plays the pattern, and writes the snapshot back. The
snapshot is also kept on disk while the alert runs, so a crash mid alert is repaired on the next
start (unless you have changed the lighting since).

## Development

.NET 10. Everything but the tray builds and tests on Linux.

```sh
dotnet build MatchAlert.sln -warnaserror
dotnet test MatchAlert.sln
dotnet publish src/MatchAlert.Tray -c Release -o dist/app   # single self-contained exe
```

| Project | Role | Depends on |
|---|---|---|
| `MatchAlert.Domain` | Colors, patterns, and the ports: `IGameEvents`, `IDeviceSource`, `ILightingDevice` | nothing |
| `MatchAlert.App` | Settings layering and validation, `PatternPlayer`, `AlertService` | Domain |
| `MatchAlert.Lcu` | The League client adapter | Domain |
| `MatchAlert.Devices` | HID, the driver seam, the VIA driver, discovery, crash recovery, setup flow | Domain, App |
| `MatchAlert.Tray` | Composition root, tray menu, wizard dialogs | all |

### Adding a keyboard

A VIA keyboard needs no code: a profile JSON in `src/MatchAlert.App/BuiltIn/devices/` (or the
wizard's output) is enough. Send one in a pull request once it is verified on the hardware.

### Adding another maker's protocol

Implement `IDeviceDriver` in `MatchAlert.Devices` (say `Drivers/Razer`), add it to the driver list in
`Program.Devices`, and write profiles with `"driver": "<your id>"`. Driver-specific settings go in a
block named after the driver, the way VIA uses `"via": { "channel": 3 }`; the core never reads it.
The VIA driver and its byte-level simulator in the tests are the pattern to follow.

### Tools

- `tools/lcu-ws.ps1` dumps raw client websocket events with timestamps, for poking at the LCU.
- `tools/make-icon.py` draws `assets/app.ico`.

## Credits

The VIA protocol details and the Keychron Q1 HE 8K's quirks were worked out on real hardware by
[kbd-signal](https://github.com/Sora-bluesky/kbd-signal) (MIT). This is a C# reimplementation of
those findings. The HID enumeration is adapted from
[OpenInzone](https://github.com/penguinwokrs/openinzone). See [NOTICE](NOTICE).

## License

GPL-3.0. See [LICENSE](LICENSE).
