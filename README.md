# deepcool-lite

Minimal Windows driver for the **DeepCool AK400 DIGITAL SE** display (USB `3633:0001`).
Shows CPU temperature and/or CPU usage. No UI, no telemetry, about 250 lines of C# in `src/`.

Scope is deliberately small: AK400 DIGITAL SE only, Celsius only, CPU only, no alarm, no tray icon.

## Requirements (Windows 10/11 x64)
- **.NET 10 Runtime** (the plain runtime, not the Desktop runtime). The release exe is framework-dependent to stay small.
- **PawnIO** driver 2.0 or newer. LibreHardwareMonitorLib uses it to read the CPU sensors; without it there is no temperature.
- **Administrator** rights (driver access). The exe asks for elevation itself.
- Quit/uninstall the official DeepCool Digital app first. Two programs must not write to the cooler.

## Get it
**Download** `deepcool-lite.exe` and `SHA256SUMS.txt` from the [Releases page](../../releases). The exe is not code-signed, so
SmartScreen may warn: choose *More info*, then *Run anyway*. Check the file first (PowerShell):

    (Get-FileHash .\deepcool-lite.exe -Algorithm SHA256).Hash.ToLower()
    Get-Content .\SHA256SUMS.txt        # the two hashes must match

**Or build it** (.NET 10 SDK):

    dotnet publish -c Release
Result: `bin\Release\net10.0\win-x64\publish\deepcool-lite.exe` (the win-x64 / single-file / framework-dependent settings live in the csproj).

## Run (elevated terminal)
    .\deepcool-lite.exe --demo          # step 1: USB path only, no sensors. Expect "88" and 5 bar segments.
    .\deepcool-lite.exe                 # step 2: digits = CPU temp, bar = CPU usage
    .\deepcool-lite.exe --mode usage    # digits = CPU usage %
    .\deepcool-lite.exe --mode auto     # digits alternate temp/usage every 5 s
Options: `--interval <ms>` (default 1000, min 200), `--layout auto|se|std` (see below), `--help`.
Only one copy can run at a time (a second one exits with code 3), so stop the boot task before testing by hand.

If the cooler is unplugged, or the PC sleeps and wakes, the program reconnects by itself.

## Start at boot (SYSTEM, hidden, before login)
Use PowerShell (elevated). Do **not** use `schtasks /create` for this: tasks it creates are stopped by Windows after 3 days.

    $exe = 'C:\Tools\deepcool-lite\deepcool-lite.exe'
    Register-ScheduledTask -TaskName DeepCoolLite -Force `
      -Action    (New-ScheduledTaskAction -Execute $exe -Argument '--mode auto') `
      -Trigger   (New-ScheduledTaskTrigger -AtStartup) `
      -Principal (New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest) `
      -Settings  (New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1))
    Start-ScheduledTask DeepCoolLite

Stop: `Stop-ScheduledTask DeepCoolLite`. Remove: `Unregister-ScheduledTask DeepCoolLite -Confirm:$false`.

## How the report ID is chosen (`--layout`)
The SE and the regular AK400 DIGITAL use the same bytes, but a unit may or may not use a HID report ID, and Windows
rejects a write whose first byte is not an ID the device declares ("The parameter is incorrect"). So `--layout auto`
(default) reads the device's HID report descriptor: report ID `0x10` if it declares one, otherwise the unnumbered
layout (ID 0). If the descriptor cannot be read it falls back to the USB product name (`AK...` = `0x10`, anything else = 0) and
tries the other one if the write is refused. The program prints which ID worked (`connected: ... report ID 0x..`).
If nothing works it prints what it found, once per outage: paste those lines into an issue.
To force one: `--layout std` (ID `0x10`) or `--layout se` (ID 0).

## Verify resource use (PowerShell, while it runs)
    $p = Get-Process deepcool-lite
    "{0:N1} MB working set, {1:N1} MB private" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)
    $a = $p.TotalProcessorTime; Start-Sleep 60; $p.Refresh()
    "{0:N2} % of one core over 60 s" -f ((($p.TotalProcessorTime - $a).TotalSeconds / 60) * 100)

## Known limitations
- **PawnIO is required** for the CPU temperature. It is a separate third-party driver that is not bundled; install it first.
  Without it (or without admin rights) the program stops with "No readable CPU temperature sensor".
- **Some units will not work: the "#119 variant".** Per the upstream Linux project's issue #119
  ([Nortank12/deepcool-digital-linux](https://github.com/Nortank12/deepcool-digital-linux)), a unit with report IDs 2-5 and 16 in its
  HID descriptor behaves differently, and that is not solved upstream either. If `--demo` fails with both `--layout se` and `--layout std`,
  you probably have it.
- Needs the .NET 10 Runtime installed (the exe is not self-contained). The exe is unsigned.
- AK400 DIGITAL SE only; Celsius only; CPU only. No alarm, Fahrenheit, GPU, tray icon or other models.
- There is no log file: run it in a terminal first when something is wrong.

## Troubleshooting
- "No readable CPU temperature sensor": not elevated, or PawnIO missing/too old.
- "already running": another copy (probably the `DeepCoolLite` task) holds the cooler. Stop it first.
- `IOException ... The parameter is incorrect` or "not connected, retrying": the lines printed under it show every HID interface found and each report ID tried. Paste them into an issue.
- Display shows only the logo with `--demo`: try `--layout std`, then `--layout se`. If both fail, see the #119 note above.
- "cooler not found or could not be opened": check `3633:0001` in Device Manager > Human Interface Devices, and that the official app is closed.

## Tests (any OS with the .NET 10 SDK and Python 3; run from the repo root)
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-packet-tests.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\version-tests.ps1
`tests/ref_se.py` is a line-by-line port of the SE packet code from the open-source Linux tool;
`tests/PacketTests` dumps this project's frames for temp 0-255 x usage 0-100 x both modes;
the two dumps (51,713 lines) must be byte-identical. CI runs both scripts on every push and pull request.

## Releasing (maintainers)
The version lives in one place: `<Version>` in `deepcool-lite.csproj`.

1. **Actions > Cut release > Run workflow**, Branch **main**.
2. Pick `patch` / `minor` / `major`, or type an exact version (`0.2.0`, or `0.2.0-rc1` for a pre-release).
   The very first release uses the version already in the csproj (0.1.0) as it is.
3. Run. It tests and builds first; only if that passes does it commit the version bump, tag `vX.Y.Z` on main, and create the
   GitHub release with `deepcool-lite.exe` and `SHA256SUMS.txt`.

Repository settings it needs: Settings > Actions > General > *Workflow permissions* can stay on **Read repository contents**
(the workflow asks for `contents: write` itself); *Allow all actions* (or at least GitHub-authored actions); and if main has a branch
rule, `github-actions[bot]` must be allowed to push to it. `.github/dependabot.yml` keeps the pinned action versions current.

## Protocol (64-byte payload, report ID not part of it for SE)
| byte | meaning |
|---|---|
| 0 | 170 init/animation, 19 temperature (C), 76 usage |
| 1 | status bar 1-10 (usage: <15% -> 1, else nearest 10%) |
| 2,3,4 | hundreds, tens, ones digit |
| 5 | alarm (unused here) |

Regular (non-SE) AK400 DIGITAL: same bytes behind report ID 0x10.

## License
MIT, see [LICENSE](LICENSE). Third parties: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0)
and [HidSharp](https://www.nuget.org/packages/HidSharp) (Apache-2.0) are linked into the exe; PawnIO is a separate driver with its own license.
`tests/ref_se.py` is derived from GPL-3.0 code (deepcool-digital-linux), stays GPL-3.0, and is test-only: it is not part of the released exe.
