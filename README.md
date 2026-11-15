# DeepCool Lite

DeepCool Lite is a small program for Windows that makes the display on your **DeepCool AK400 DIGITAL SE** CPU cooler show your
processor's **temperature**, its **usage**, or **both, taking turns**. It lives in the tray (next to the clock), uses very little
memory, never connects to the internet, and does not need the official DeepCool software.

> It only works with the **AK400 DIGITAL SE**. The regular AK400 DIGITAL, the AK400 DIGITAL PRO and other coolers are not supported:
> the program checks which cooler it is talking to and does nothing if it is not the SE.

## What you need
- Windows 10 or 11 (64-bit) and an administrator account
- The AK400 DIGITAL SE, with its USB cable plugged into the motherboard

Nothing else has to be installed first. (Please close the official DeepCool software: only one program can control the display at a time.)

## Install
1. Download **`deepcool-lite-setup-<version>.exe`** from the [Releases page](../../releases).
2. Run it. Windows may say *"Windows protected your PC"* because the file is not signed: choose **More info**, then **Run anyway**.
   (To check the download, compare `Get-FileHash` of the file in PowerShell with the number in `SHA256SUMS.txt`.)
3. Follow the steps. One page asks whether to install **PawnIO**, a small helper that lets programs read the CPU temperature.
   It is installed **only if you tick the box**. Without it the display can still show CPU usage, just not the temperature.
4. Leave **Start DeepCool Lite when I sign in to Windows** ticked if you want it to start by itself. Windows will ask for your
   permission once, during the installation, and never again at start-up.

## Use it
Click the DeepCool Lite icon in the tray (or right-click it and choose **Open DeepCool Lite**). If you don't see it, click the small
arrow in the taskbar to show hidden icons.

- **What the cooler shows:** *Temperature*, *CPU usage*, or *Both, alternating*. For *Both*, choose how often it switches (3, 5, 7 or 10 seconds).
  The row of bars on the display always follows CPU usage.
- **Start with Windows:** turn this on or off at any time.
- **Status:** tells you if the cooler is connected, and if the temperature is unavailable.
- **Closing the window** does not stop the program: it keeps running in the tray. To stop it, right-click the tray icon and choose **Quit**.

Your choices are saved automatically.

## The display is blank
1. Close the official DeepCool software, then quit and start DeepCool Lite again. Only one program can control the display.
2. Open DeepCool Lite and read **Status**. *Cooler not found* means check the USB cable. *Unsupported device* means this is not the AK400 DIGITAL SE.
3. If it says the **CPU temperature is unavailable**, run the installer again and tick the PawnIO box. Until then, choose *CPU usage*.

## Uninstall
Settings > Apps > Installed apps > **DeepCool Lite** > Uninstall. PawnIO is left installed, because other programs may use it;
you can remove it from the same list.

## Privacy
DeepCool Lite has no network access, no tracking and no automatic updates. It saves one small settings file in
`%LOCALAPPDATA%\DeepCoolLite`.

## No warranty
DeepCool Lite is free software and comes **with no warranty**. You use it at your own risk. It is not made or endorsed by DeepCool.
The details are in the [MIT License](LICENSE). PawnIO is a separate product by its own author, with its own license.

## For developers
How it works, how to build it and how releases are made: [docs/development.md](docs/development.md).
