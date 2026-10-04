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

| Keyboard | Status |
|---|---|
| Keychron Q1 HE 8K | Verified on hardware |
| Pulsar PCMK 2HE TKL | **Unverified**: built from Pulsar's own configurator, not tested on a board |
| Pulsar XBOARD MS | **Unverified**: same |
| Other Pulsar keyboards on the same protocol | Set up automatically by the wizard, unverified |
| Other keyboards with VIA support | Set up with the wizard (below) |

### Pulsar keyboards

Pulsar's current keyboards are not VIA keyboards: they are configured with Pulsar's web app,
Bibimbap. The PCMK 2HE TKL and XBOARD MS use a lighting protocol of their own on the same raw HID
interface, which this app speaks as Bibimbap does. Nobody has tried it on a real board yet, so it is
careful: it never sends Bibimbap's "save" command, never touches the bootloader, and by default plays the
keyboard's own breathing effect so it writes to the keyboard only once per alert.

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
