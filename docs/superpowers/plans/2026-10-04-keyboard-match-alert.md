# Keyboard Match Alert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A tray app that flashes the keyboard when League of Legends finds a match and restores it afterwards.

**Architecture:** Clean architecture in five projects. Domain owns the ports (`IGameEvents`, `IDeviceSource`, `ILightingDevice`); App holds settings and the alert use case; Lcu and Devices are adapters; Tray is the composition root. See `docs/superpowers/specs/2026-10-04-keyboard-match-alert-design.md`.

**Tech Stack:** .NET 10, C#, WinForms (tray only), xUnit, `Microsoft.Extensions.TimeProvider.Testing`, Inno Setup.

## Global Constraints

- `net10.0` everywhere except `MatchAlert.Tray` (`net10.0-windows`, `UseWindowsForms`). The test project references every project except Tray and runs on Linux.
- No NuGet dependencies in `src/`. Tests may use xUnit and `Microsoft.Extensions.TimeProvider.Testing`.
- Every source file starts with `// SPDX-License-Identifier: GPL-3.0-only` and `// Copyright (C) 2026 penguinwokrs`.
- VIA save (`0x09`) is never sent. Only set (`0x07`), get (`0x08`) and protocol (`0x01`).
- Build with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH; dotnet build MatchAlert.sln -warnaserror`.
- Test with `dotnet test MatchAlert.sln`.
- Commits carry no `Co-Authored-By` line.

---

### Task 1: Solution and Domain

**Files:**
- Create: `MatchAlert.sln`, `Directory.Build.props`, `src/MatchAlert.Domain/{MatchAlert.Domain.csproj,Color.cs,Pattern.cs,Ports.cs}`, `tests/MatchAlert.Tests/{MatchAlert.Tests.csproj,Domain/ColorTests.cs}`

**Interfaces (produced):**
```csharp
readonly record struct Rgb(byte R, byte G, byte B) { static Rgb Parse(string hex); Hsv ToHsv(); }
readonly record struct Hsv(byte H, byte S, byte V);              // 0-255 wheel, red 0, green 85, blue 170
sealed record Step(Rgb Color, int Brightness, string Effect, byte? Speed, int DurationMs);
sealed record Pattern(IReadOnlyList<Step> Steps, int? RepeatCount);   // null = until stopped
sealed record ClientState(bool Connected, string? Phase) { bool IsReadyCheck { get; } }
interface IGameEvents { IAsyncEnumerable<ClientState> WatchAsync(CancellationToken ct); }
interface ILightingDevice { string Id; string Name; int MinStepMs; ILightingSession OpenSession(); }
interface ILightingSession : IDisposable { void Show(Step step); }   // Dispose restores
interface IDeviceSource { IReadOnlyList<ILightingDevice> Discover(); void RecoverInterruptedSessions(); }
```

- [x] Write `ColorTests`: `#FF0000 -> Hsv(0,255,255)`, `#00FF00 -> (85,255,255)`, `#0000FF -> (170,255,255)`, `#FFFFFF -> (0,0,255)`, `#800000 -> V 128`, lowercase and no `#` accepted, `#GGG000` and `#FFF` throw `FormatException`.
- [x] Run, see them fail to compile; implement; run, see them pass.
- [x] Commit `Domain: colors, patterns and the ports`.

### Task 2: Settings

**Files:**
- Create: `src/MatchAlert.App/{MatchAlert.App.csproj,DeviceProfile.cs,SettingsModel.cs,SettingsLoader.cs,SettingsException.cs,BuiltIn/patterns.json,BuiltIn/devices/keychron-q1-he-8k.json}`, `tests/MatchAlert.Tests/App/SettingsLoaderTests.cs`

**Interfaces (produced):**
```csharp
sealed record DeviceMatch(ushort VendorId, IReadOnlyList<ushort> ProductIds, string? ProductString);
sealed class DeviceProfile { string Id; string Name; string Driver; DeviceMatch Match;
    IReadOnlyDictionary<string,int> Effects; int MinStepMs; string? DefaultPattern;
    IReadOnlyDictionary<string, JsonElement> Options; }          // driver-specific, e.g. "via"
sealed class ResolvedSettings { IReadOnlyDictionary<string,Pattern> Patterns; IReadOnlyList<DeviceProfile> Profiles;
    TimeSpan MaxAlert; Pattern PatternFor(DeviceProfile p); bool IsEnabled(string deviceId); }
static class SettingsLoader {
    ResolvedSettings Load(SettingsSources sources);          // throws SettingsException("<source>: <path>: <reason>")
    string Serialize(DeviceProfile profile); }
sealed record SettingsSources(IReadOnlyList<SourceText> BuiltInProfiles, SourceText? UserSettings,
    IReadOnlyList<SourceText> UserProfiles) { static SettingsSources FromDisk(string settingsDir); }
sealed record SourceText(string Name, string Json);
```

- [x] Tests: defaults resolve the Q1 HE to `match-found` (2 steps, 300 ms, `#FF0000`/`#FFFFFF`); `settings.pattern` selects a built-in; a user pattern replaces a built-in of the same name; `devices[id].pattern` beats `settings.pattern`; a user profile replaces the built-in profile with the same id; comments and trailing commas parse; `enabled:false` is reported by `IsEnabled`.
- [x] Error tests, each asserting the message names the field path: unknown effect for a device (`patterns.p.steps[0].effect`), brightness 150, missing `durationMs` on a 2-step pattern, unknown pattern name, bad color, `repeat: 0`.
- [x] Implement, pass, commit `App: layered settings and device profiles`.

### Task 3: Alert use case

**Files:**
- Create: `src/MatchAlert.App/{PatternPlayer.cs,AlertService.cs}`, `tests/MatchAlert.Tests/App/{PatternPlayerTests.cs,AlertServiceTests.cs,Fakes.cs}`

**Interfaces (produced):**
```csharp
static class PatternPlayer { Task PlayAsync(ILightingSession s, Pattern p, int minStepMs, TimeProvider t, CancellationToken ct); }
enum AlertStatus { WaitingForClient, Connected, Alerting }
sealed class AlertService(IGameEvents events, IDeviceSource devices, Func<ResolvedSettings> settings,
    TimeProvider time, Action<string> log) {
    event Action<AlertStatus>? StatusChanged; AlertStatus Status { get; }
    Task RunAsync(CancellationToken ct); Task TestAsync(TimeSpan duration, CancellationToken ct); }
```

- [x] PatternPlayer tests on `FakeTimeProvider`: blink shows A,B,A,B at 0/300/600/900 ms; `minStepMs` 500 stretches 300 ms steps; `RepeatCount 2` holds the last step; single step is shown once; cancellation ends the task.
- [x] AlertService tests with a channel-backed fake `IGameEvents` and recording fake devices: `Lobby` opens nothing; `ReadyCheck` opens one session per enabled device and shows steps; leaving `ReadyCheck` disposes; disconnect disposes; `MaxAlert` disposes while still in `ReadyCheck`; a device whose `OpenSession` throws does not stop the others; status events go `WaitingForClient -> Connected -> Alerting -> Connected`.
- [x] Implement, pass, commit `App: alert service and pattern player`.

### Task 4: League client adapter

**Files:**
- Create: `src/MatchAlert.Lcu/{MatchAlert.Lcu.csproj,Lockfile.cs,LcuFrames.cs,LcuGameEvents.cs}`, `tests/MatchAlert.Tests/Lcu/{LockfileTests.cs,LcuFramesTests.cs}`

**Interfaces (produced):**
```csharp
sealed record Lockfile(int Port, string Password) { static Lockfile Parse(string content); }
static class LcuFrames { const string PhaseEvent; bool TryParsePhase(string frame, out string phase); }
sealed class LcuGameEvents(Func<string?> findLockfile, Action<string> log) : IGameEvents {
    static string? FindRunningClientLockfile(); }
```

- [x] Tests: lockfile `LeagueClient:21648:52364:pw:https` parses; malformed throws `FormatException`. Frames recorded on 2026-10-04 (`Matchmaking`, `ReadyCheck`, `Lobby`) parse; a `ready-check` frame, a subscribe ack, an empty string and garbage return false.
- [x] Implement the websocket loop: yield `ClientState(false,null)` while no client; connect, subscribe, `GET` current phase, yield `(true, phase)`; yield each phase; on close or error yield disconnected and retry after 3 s.
- [x] Commit `Lcu: gameflow-phase over the client websocket`.

### Task 5: HID and the VIA protocol

**Files:**
- Create: `src/MatchAlert.Devices/{MatchAlert.Devices.csproj,Hid/HidDeviceInfo.cs,Hid/IHidBus.cs,Hid/NativeMethods.cs,Hid/WindowsHidBus.cs,Via/ViaKeyboard.cs,Via/ViaSnapshot.cs}`, `tests/MatchAlert.Tests/Devices/{FakeViaBoard.cs,ViaKeyboardTests.cs}`

**Interfaces (produced):**
```csharp
sealed record HidDeviceInfo(string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    int InputReportLength, int OutputReportLength, string Product);
interface IRawHid : IDisposable { void Write(ReadOnlySpan<byte> payload); byte[]? Read(int timeoutMs); }  // 32-byte payloads
interface IHidBus { IReadOnlyList<HidDeviceInfo> Enumerate(); IRawHid Open(HidDeviceInfo d); }
sealed record ViaSnapshot(byte Brightness, byte Effect, byte Speed, byte Hue, byte Sat);
sealed record ViaTiming(int ColorHoldMs = 200, int BrightnessHoldMs = 400, int SettleMs = 30, int BudgetMs = 1500, int FastEchoMs = 20);
sealed class ViaKeyboard(IRawHid hid, int channel, bool resetOnEffect, ViaTiming timing) : IDisposable {
    int Protocol; bool IsV3; ViaSnapshot Snapshot(); bool Apply(ViaSnapshot target);
    void ShowColor(byte hue, byte sat, byte brightness);     // fast path, no effect change
    bool VerifyChannel(); bool ProbeResetOnEffect(byte otherEffect); }
```

- [x] `FakeViaBoard : IRawHid` speaks VIA at the byte level: protocol 9 or 13, a channel, optional reset quirk armed N writes after an effect change, optional lossy brightness (`max`), a queue of foreign echoes, strict framing (throws on malformed packets).
- [x] Tests: v3 set framing `07 03 02 ..`; v2 framing `07 81 ..`; protocol read from `01`; foreign echoes are skipped; `Snapshot` reads all four; `Apply` with the quirk ends with the target color and brightness; `ShowColor` sends no effect write; `VerifyChannel` true on a matching channel, false when the board ignores channel 3, and restores speed; no packet ever starts with `09`.
- [x] `WindowsHidBus`: SetupAPI enumeration from openinzone, overlapped read/write, report id `0x00` + payload padded to the output report length.
- [x] Commit `Devices: raw HID and the VIA protocol`.

### Task 6: VIA lighting device, recovery, discovery and setup

**Files:**
- Create: `src/MatchAlert.Devices/{IDeviceDriver.cs,HidDeviceSource.cs,PendingSnapshots.cs,Via/ViaDriver.cs,Via/ViaLightingDevice.cs,Via/ViaSetupFlow.cs,Setup/IUserPrompt.cs}`, tests `Devices/{ViaSessionTests.cs,HidDeviceSourceTests.cs,ViaSetupFlowTests.cs,PendingSnapshotsTests.cs}`

**Interfaces (produced):**
```csharp
interface IDeviceDriver { string Id; bool IsControlInterface(HidDeviceInfo d);
    ILightingDevice Create(HidDeviceInfo d, DeviceProfile p); ISetupFlow? TrySetup(HidDeviceInfo d); }
interface ISetupFlow { DeviceProfile? Run(IUserPrompt prompt); }
interface IUserPrompt { string? Ask(string question, IReadOnlyList<string> choices); void Inform(string message); }
sealed class HidDeviceSource(IHidBus bus, IReadOnlyList<IDeviceDriver> drivers, Func<ResolvedSettings> settings,
    PendingSnapshots pending, Action<string> log) : IDeviceSource {
    IReadOnlyList<HidDeviceInfo> Unrecognised(); ISetupFlow? SetupFor(HidDeviceInfo d); }
sealed class PendingSnapshots(string dir) { void Save(string key, ViaSnapshot s, IEnumerable<(byte Effect,byte Hue,byte Sat)> ours);
    void Delete(string key); IReadOnlyList<PendingEntry> All(); }
```

- [x] Session tests: a two-step solid pattern writes the effect once; dispose restores the snapshot; a second session on a lossy board restores the brightness the first session read, not one lower; a pending file exists during the session and is gone after.
- [x] Recovery tests: leftover restored when the board still shows one of our steps; discarded when it does not.
- [x] Source tests: the Q1 HE profile matches PID `0x1012` on usage page `FF60`; the Link-KM dock (`0xD026`) is unrecognised; a disabled device is not discovered.
- [x] Setup tests with a scripted prompt: v3 board, answers neither/steady/pulsing gives `solid`, `breathing` and `resetOnEffect`; a board that fails channel verification informs "not supported" and returns null without writing outside channel 3; cancelling restores the lighting.
- [x] Commit `Devices: VIA sessions, crash recovery, discovery and setup`.

### Task 7: Tray

**Files:**
- Create: `src/MatchAlert.Tray/{MatchAlert.Tray.csproj,Program.cs,TrayContext.cs,TrayIcons.cs,TaskDialogPrompt.cs,FileLog.cs,Autostart.cs,SettingsService.cs,AppPaths.cs}`
- Delete: `lol-match-alert.ps1`

- [x] Composition root; single-instance mutex; `--test` headless cycle that logs before and after values and exits non-zero on mismatch (brightness tolerance 2 on v3).
- [x] Tray menu per the spec; runtime-drawn icons for the three statuses; `TaskDialog`-based prompt for the wizard, running the flow off the UI thread.
- [x] `SettingsService`: first-run commented template, `FileSystemWatcher` with 300 ms debounce, last-good on error.
- [x] Build with `-warnaserror`, publish win-x64 single file, run `--test` against the Q1 HE 8K, record the before/after values, set `minStepMs` from the measurement.
- [x] Commit `Tray: composition root, tray menu and setup wizard`.

### Task 8: Ship it

**Files:**
- Create: `installer/lol-match-alert.iss`, `.github/workflows/{ci.yml,release.yml}`, `.github/ISSUE_TEMPLATE/keyboard-support.yml`, `NOTICE`
- (`assets/app.ico` and `tools/make-icon.py` landed with Task 7, which needed the icon. No `installer/build.sh`: the release workflow is the build, and a local run is one ISCC call.)
- Modify: `README.md`, `.gitignore`

- [x] CI: build `-warnaserror` and test on ubuntu. Release on `v*`: test, publish, Inno Setup, zip, attach.
- [x] README for players (install, what it does, customise, add a keyboard) and for contributors (architecture, adding a driver). NOTICE crediting kbd-signal.
- [x] Commit `Ship: installer, CI and release workflow`; push; open the PR.
