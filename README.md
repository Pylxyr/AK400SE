# DeepCool Lite

DeepCool Lite is a small tray application for Windows that shows CPU temperature and CPU usage on the status display of the
**DeepCool AK400 DIGITAL SE** CPU cooler. It is an independent replacement for the official DeepCool software for that one purpose.

## What it does
- Display modes: temperature, CPU usage, or both alternating at a chosen interval (3, 5, 7 or 10 seconds). The bar on the display always follows CPU usage.
- Temperature unit: Celsius or Fahrenheit.
- Runs from a tray icon. Closing the settings window hides it to the tray; the tray menu opens the window or quits the program.
- Optional start with Windows. Administrator rights are granted once, by the installer, through a scheduled task, so no UAC prompt appears at start-up.
- Ships as a single installer with the .NET runtime included. Nothing else has to be installed first.
- Settings are stored in one small file, `%LOCALAPPDATA%\DeepCoolLite\settings.json`.

## Supported hardware and requirements
- Cooler: AK400 DIGITAL SE only (USB `3633:0001`, product name `A400-DIGITAL`). The regular AK400 DIGITAL, the AK400 DIGITAL PRO and other models share parts of the protocol but are not supported and are left untouched.
- Windows 10 or 11, 64-bit, with an administrator account (needed to read the CPU temperature).
- Only one program should drive the display at a time, so the official DeepCool software should not run at the same time.

## Safety design
- The device is verified before anything is written: vendor ID, product ID, product name and the report layout declared by the device must all match. If a check fails, or the cooler is missing, nothing is sent and the window and tray show the status (`Cooler not found`, `Unsupported device: <name>`). No other USB or HID device is ever written to.
- Every value sent to the display is clamped, writes are rate-limited, and there is no way to send arbitrary data to the device.
- Hardware access is read-only and limited to the CPU group of LibreHardwareMonitorLib: no fan control, embedded controller, Super I/O or other components.
- No network access, no telemetry and no automatic updates.
- CPU temperature needs the third-party PawnIO driver. The installer includes the unmodified official PawnIO installer, verifies its SHA-256 checksum, and installs it only after explicit consent on a dedicated page (the regular signed edition only). PawnIO is left in place on uninstall because other programs may use it. Without PawnIO the display can show CPU usage, and the window states that the temperature is unavailable.

## Disclaimers
- **No warranty.** DeepCool Lite is free software provided "as is", without warranty of any kind, and is used at the user's own risk. The terms are those of the [MIT License](LICENSE).
- **Unofficial.** The project is not affiliated with, endorsed by or supported by DeepCool. Product and company names belong to their respective owners. The display modes follow publicly available information about the official software and may differ from it in details.
- **Limited testing.** Development and testing were done on a single AK400 DIGITAL SE unit. Other units, firmware or PC configurations may behave differently. Some units are known not to work with this protocol (see the notes in [docs/development.md](docs/development.md)).
- **Third-party components.** PawnIO is a separate driver by its own author, under its own license. LibreHardwareMonitorLib (MPL-2.0), HidSharp (Apache-2.0), the .NET runtime and Windows Forms (MIT) are bundled.
- **Unsigned installer.** The installer is not code-signed, so Windows SmartScreen may show a warning. A SHA-256 checksum is published with each release.

## More
- Installers and checksums: [Releases](../../releases)
- Build, architecture and release process: [docs/development.md](docs/development.md)
- License: [MIT](LICENSE)
