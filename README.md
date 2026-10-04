# lol-match-alert

Toast notification and alarm sound the moment League of Legends finds a match,
so you never miss the ready check while tabbed out.

A single PowerShell script. No install, no dependencies, runs on the Windows PowerShell
that ships with Windows.

> **Not affiliated with Riot Games.** This uses the unofficial, undocumented LCU API of
> the League client. It only *reads* the client's state. It does not accept the ready
> check for you.

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File lol-match-alert.ps1
```

Leave it running. It waits for the League client, reconnects when the client restarts,
and alerts once per match. Fire the alert once to check sound and toast:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File lol-match-alert.ps1 -Test
```

Pick a different sound with `-Sound C:\Windows\Media\Ring01.wav` (any `.wav`).

`-ExecutionPolicy Bypass` is needed because files downloaded from GitHub are marked as
coming from the internet and blocked by the default policy.

## How it works

The League client exposes a local WebSocket (WAMP 1.0) on the port and password written to
its `lockfile`. The script subscribes to one event,
`OnJsonApiEvent_lol-gameflow_v1_gameflow-phase`, and alerts when the phase becomes
`ReadyCheck`. Measured on a live client, that event fires in the same millisecond as the
ready-check state becomes `InProgress`, and exactly once per match.

Lifecycle for reference: `Lobby → Matchmaking → ReadyCheck → ChampSelect → GameStart → InProgress`.
Custom games skip `ReadyCheck`, so no alert there.

The sound plays through `System.Media.SoundPlayer` and the toast is sent with its own audio
muted, because Windows Focus Assist silences toast audio but not SoundPlayer.

## Poking at the LCU yourself

`tools/lcu-ws.ps1` dumps raw events with timestamps:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\lcu-ws.ps1 OnJsonApiEvent_lol-gameflow_v1_gameflow-phase OnJsonApiEvent_lol-matchmaking_v1_ready-check
```

Pass `OnJsonApiEvent` alone for every event the client emits. The full schema
(events, endpoints, types) is served by the running client at
`GET https://127.0.0.1:<port>/help?format=Full` with basic auth `riot:<password>`.

## License

GPL-3.0. See [LICENSE](LICENSE).
