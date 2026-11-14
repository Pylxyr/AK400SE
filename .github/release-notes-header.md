## Install

`deepcool-lite.exe` drives the **DeepCool AK400 DIGITAL SE** display (USB `3633:0001`). Windows 10/11 x64 only.

1. Install the **.NET 10 Runtime** (the plain runtime, not the Desktop runtime) and the **PawnIO** driver 2.0 or newer.
2. Quit the official DeepCool Digital app: two programs must not write to the cooler.
3. In an **elevated** terminal run `deepcool-lite.exe --demo`. The display should show **88** and five bar segments. Then run it without options for CPU temperature and usage.

**Windows SmartScreen:** the exe is not code-signed, so Windows may show "Windows protected your PC". Choose **More info**, then **Run anyway**. To check the file first, compare `Get-FileHash .\deepcool-lite.exe` with `SHA256SUMS.txt` (PowerShell).

Full instructions, start-at-boot, troubleshooting and known limitations are in the README.
