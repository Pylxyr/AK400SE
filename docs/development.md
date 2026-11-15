# AK400SE: development notes

This file holds the technical details that used to be the README. The [README](../README.md) is for people who just want to use the app.

AK400SE is a small Windows tray app that drives the display of the **DeepCool AK400 DIGITAL SE** cooler (USB `3633:0001`).
It shows CPU temperature and/or CPU usage. Earlier versions were a console program with command-line options; those are gone: the app
is configured in its window (settings in `%LOCALAPPDATA%\AK400SE\settings.json`).

Scope is deliberately small: AK400 DIGITAL SE only, CPU only, no alarm, no other cooler models.

## How it works
- `src/Program.cs`: entry point. Started normally, it asks Task Scheduler to start the installed task `AK400SE`, which runs the same
  exe **elevated without a UAC prompt** (see "Start with Windows"). A second copy exits quietly (named mutex): one writer to the cooler.
  The only command-line forms are the installer hooks `--task on|off|remove` and the task's own marker `--from-task`.
- `src/Ui/`: WinForms tray icon + menu (`TrayApp`), the settings window (`MainForm`, created when opened and disposed when closed),
  the scheduled-task helper (`StartTask`).
- `src/Engine.cs`: one tick per second on a background thread: find the cooler, read the CPU, choose what to show, write one frame.
  It works through small interfaces and a clock, so it is unit-tested with fakes.
- UI-free logic with tests: `DeviceCheck.cs` (device verification), `WriteGuard.cs` (minimum write interval), `ModeScheduler.cs`,
  `Settings.cs`, `Status.cs`, `TaskXml.cs`, plus `Packet.cs` and `HidDescriptor.cs`.
- Hardware: `Display.cs` (HidSharp) and `Cpu.cs` (LibreHardwareMonitorLib, CPU group only).

### Safety rules (enforced in code, covered by `tests/LogicTests`)
- A write happens only to a HID interface with VID `0x3633`, PID `0x0001`, product string exactly `A400-DIGITAL`, and an Output report ID
  taken from its own descriptor. The regular AK400 DIGITAL (`AK400-DIGITAL`) shares VID/PID and is refused. Every other HID device is never opened.
- Status is reported in the window and in the tray tooltip: `Cooler not found`, `Unsupported device: <product string>`, etc. The search repeats quietly every 5 s.
- There is no way to give `Display` raw bytes: the only write is `Show(...)`, which builds a clamped frame. Frames are at least 500 ms apart.
- When there is no temperature (PawnIO missing), nothing is written for it; the display never shows a made-up `0`.
- LibreHardwareMonitorLib runs with only `IsCpuEnabled`: no motherboard / Super I/O, embedded controller, fan control, GPU, storage, memory, network, battery or PSU.
- No network access, telemetry or update check. The only file written is the settings JSON (a hand-edited file is clamped to valid values on load).

## Build
.NET 10 SDK, Windows.

    dotnet publish -c Release -o artifacts/publish      # self-contained win-x64 folder (runtime included)
    ./scripts/build-installer.ps1 -Version 0.2.0        # publish + pinned PawnIO + Inno Setup -> artifacts/installer/

WinForms cannot be trimmed (the SDK refuses), so the .NET runtime and WinForms ship whole and the installer compresses them
(LZMA2). ReadyToRun is on for the app's own code. Inno Setup 6 is preinstalled on GitHub's `windows-latest`; the script installs it with Chocolatey if missing.

### The installer (`installer/AK400SE.iss`)
- Installs to `Program Files` only (no directory page): the start-up task runs the app with administrator rights, so the exe must not be replaceable by a normal user. The app also refuses to create the task for an exe outside Program Files.
- Creates the task once (`AK400SE.exe --task on|off`), because the installer is elevated. Afterwards no UAC prompt is needed to start the app.
- **PawnIO**: the installer contains the unmodified official `PawnIO_setup.exe` (release pinned in the `.iss`; `scripts/get-pawnio.ps1` downloads it from `github.com/namazso/PawnIO.Setup` and aborts if the SHA-256 differs). A dedicated page asks for consent (unchecked by default, skipped if PawnIO is already installed). Setup checks the SHA-256 again and runs it with `-install -silent` only. The "unrestricted" edition (test-signed, needs boot configuration changes) is never used. Uninstalling leaves PawnIO alone.
- Versions up to 0.1.x were called "DeepCool Lite". The installer keeps the same AppId (Windows sees an upgrade), installs to the new folder, and removes the old folder, Start menu shortcut, task (`DeepCoolLite`) and process. Settings are not migrated: the first run after the rename starts from the defaults.
- Moving to a newer PawnIO: update `PawnIoVersion` and `PawnIoSha256` in the `.iss` (one place), read the release page and its redistribution terms again.

## Start with Windows (how there is no UAC prompt)
Reading the CPU temperature needs administrator rights, and the app must run in the user's session (tray icon). The scheduled task
`AK400SE` (run level *highest*, interactive token, execution time limit **unlimited**, not stopped on battery, ignore a second start,
restart up to 3 times on failure) is created by the elevated installer. The **Start with Windows** switch adds or removes its logon trigger.
`schtasks` would create tasks that Windows stops after 3 days, which is why the task is registered from XML (`src/TaskXml.cs`, tested).

Inspect it: `schtasks /Query /TN AK400SE /XML`. Start it by hand: `schtasks /Run /TN AK400SE`. Remove: `schtasks /Delete /TN AK400SE /F`.
Running the exe from a build folder (no task) falls back to one ordinary UAC prompt, and "Start with Windows" is disabled there.

## How the report ID is chosen
The SE and the regular AK400 DIGITAL use the same bytes, but a unit may or may not use a HID report ID, and Windows rejects a write
whose first byte is not an ID the device declares ("The parameter is incorrect"). So the report descriptor is read: report ID `0x10`
if it declares an Output report with that ID, otherwise the unnumbered layout (ID 0). If the descriptor cannot be read, or declares neither,
nothing is written (earlier versions guessed from the product name and retried; that fallback was removed to fail closed).

## Verify resource use (PowerShell, while it runs, window closed)
    $p = Get-Process AK400SE
    "{0:N1} MB working set, {1:N1} MB private" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)
    $a = $p.TotalProcessorTime; Start-Sleep 60; $p.Refresh()
    "{0:N2} % of one core over 60 s" -f ((($p.TotalProcessorTime - $a).TotalSeconds / 60) * 100)

## Known limitations
- **PawnIO is required** for the CPU temperature. It is a separate third-party driver; without it the window says so and CPU usage still works.
- **Some units will not work: the "#119 variant".** Per the upstream Linux project's issue #119
  ([Nortank12/deepcool-digital-linux](https://github.com/Nortank12/deepcool-digital-linux)), a unit with report IDs 2-5 and 16 in its
  HID descriptor behaves differently, and that is not solved upstream either.
- The installer and exe are unsigned (SmartScreen warns).
- AK400 DIGITAL SE only; CPU only. No alarm, GPU or other models.
- Fahrenheit sends mode byte `35` with the value converted on the PC, as the open-source Linux port of the official software does. It has been checked against that source and by tests, not yet confirmed on every unit.
- There is no log file: the window's status lines are the diagnostics.
- The auto-switch interval offered in the window is 3/5/7/10 s (default 5 s). The Linux port of the official software uses a fixed 5 s;
  an early hands-on review measured 7 s. The official Windows app was not available to compare against.

## Troubleshooting (what the status line means)
- `Cooler not found`: check `3633:0001` in Device Manager > Human Interface Devices and the USB cable.
- `Unsupported device: <name>`: the product string is not `A400-DIGITAL` (for example the regular AK400 DIGITAL), or the device declares no usable Output report.
- `Cooler found but could not be used`: another program (the official DeepCool software) has it open, or the first write was refused.
- `CPU temperature unavailable: the PawnIO driver is not installed`: install PawnIO (`winget install namazso.PawnIO`); the app notices it within about 30 s (restart Windows if the PawnIO installer asks). A driver that is installed but gives no reading shows `no readable temperature sensor`.
- "Display blank" while the status says connected: wait a few seconds (the first frame comes one tick after connecting).

## Tests (run from the repo root)
    dotnet run --project tests/LogicTests -c Release                                   # any OS with the .NET 10 SDK
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-packet-tests.ps1   # needs Python 3 too
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\version-tests.ps1
`tests/LogicTests` covers device verification, the write guard, mode scheduling, settings load/save, status text, the engine (with fake cooler and sensors), and the task XML.
`tests/ref_se.py` is a line-by-line port of the SE packet code from the open-source Linux tool;
`tests/PacketTests` dumps this project's frames for temp 0-255 x usage 0-100 x both modes;
the two dumps (51,713 lines) must be byte-identical. CI runs all of them on every push and pull request, then builds and checks the installer.

## Releasing (maintainers)
The version lives in one place: `<Version>` in `AK400SE.csproj`.

1. **Actions > Cut release > Run workflow**, Branch **main**.
2. Pick `patch` / `minor` / `major`, or type an exact version (`0.2.0`, or `0.2.0-rc1` for a pre-release).
   The very first release uses the version already in the csproj as it is.
3. Run. It tests and builds first; only if that passes does it commit the version bump, tag `vX.Y.Z` on main, and create the
   GitHub release with `AK400SE-setup-X.Y.Z.exe` and `SHA256SUMS.txt`.

Repository settings it needs: Settings > Actions > General > *Workflow permissions* can stay on **Read repository contents**
(the workflow asks for `contents: write` itself); *Allow all actions* (or at least GitHub-authored actions); and if main has a branch
rule, `github-actions[bot]` must be allowed to push to it. `.github/dependabot.yml` keeps the pinned action versions current.

## Protocol (64-byte payload, report ID not part of it for SE)
| byte | meaning |
|---|---|
| 0 | 170 init/animation, 19 temperature (C), 35 temperature (F), 76 usage |
| 1 | status bar 1-10 (usage: <15% -> 1, else nearest 10%) |
| 2,3,4 | hundreds, tens, ones digit |
| 5 | alarm (unused here) |

Regular (non-SE) AK400 DIGITAL: same bytes behind report ID 0x10. The status bar always follows CPU usage.

## License
MIT, see [LICENSE](../LICENSE). Third parties: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0)
and [HidSharp](https://www.nuget.org/packages/HidSharp) (Apache-2.0) are shipped with the app, together with the .NET runtime and Windows Forms (MIT).
PawnIO is a separate driver with its own license. Its author's wiki ([Using PawnIO Modules](https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules), "Licensing considerations") says the official binary editions are proprietary, "however redistribution of installer is allowed", and recommends pointing users to [pawnio.eu](https://pawnio.eu) instead of shipping a copy. The installer bundles the unmodified official installer and runs it only after consent; if you would rather not bundle it, replace the bundled file with a download of the same pinned release (keeping the SHA-256 check) or a link to pawnio.eu.
`tests/ref_se.py` is derived from GPL-3.0 code (deepcool-digital-linux), stays GPL-3.0, and is test-only: it is not part of the released app.
